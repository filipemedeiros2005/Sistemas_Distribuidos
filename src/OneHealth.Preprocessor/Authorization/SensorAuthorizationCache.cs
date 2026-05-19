using Npgsql;
using OneHealth.Common.Db;

namespace OneHealth.Preprocessor.Authorization;

/// <summary>
/// Authoritative check whether a sensor id is registered in the
/// <c>sensors</c> table. Positive results are cached forever (a sensor that
/// was authorised once stays authorised for the lifetime of the process);
/// negative results re-query each time so a newly-registered sensor starts
/// passing through immediately after the Gateway has UPSERTed it.
///
/// <para>
/// Thread-safety is provided by a <see cref="System.Threading.Mutex"/>
/// guarding both the in-memory cache lookup and the database probe. This is
/// the explicit "threads + mutex" requirement of the academic project —
/// concurrent Normalize calls from the Gateway can land on different thread
/// pool threads, and the cache must remain consistent.
/// </para>
/// <para>
/// The mutex is unnamed (intra-process) and therefore portable across macOS,
/// Linux, and Windows without the platform-specific quirks of the
/// <c>Global\\</c> prefix.
/// </para>
/// </summary>
public sealed class SensorAuthorizationCache : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly HashSet<uint> _authorized = new();
    private readonly Mutex _mutex = new(initiallyOwned: false);

    public SensorAuthorizationCache(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>
    /// Returns true if <paramref name="sensorId"/> exists in the
    /// <c>sensors</c> table. Cache hits avoid the DB round-trip.
    /// </summary>
    public bool IsAuthorized(uint sensorId)
    {
        _mutex.WaitOne();
        try
        {
            if (_authorized.Contains(sensorId))
                return true;

            // Cache miss — consult the registry.
            using var cmd = _dataSource.CreateCommand(SensorsSchema.IsSensorAuthorized);
            cmd.Parameters.AddWithValue((int)sensorId);
            using var reader = cmd.ExecuteReader();
            var found = reader.Read();

            if (found)
                _authorized.Add(sensorId);

            return found;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        _dataSource.Dispose();
        _mutex.Dispose();
    }
}
