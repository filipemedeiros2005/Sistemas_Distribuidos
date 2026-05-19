using Npgsql;
using OneHealth.Common.Db;

namespace OneHealth.Gateway.Registry;

/// <summary>
/// Owns the <c>sensors</c> table in PostgreSQL on behalf of the Gateway.
/// Ensures the schema exists on boot, then UPSERTs sensor rows in response
/// to Hello and Status packets — keeping <c>last_seen</c> fresh so a
/// watchdog can detect dead sensors.
///
/// The Preprocessor reads this table (read-only) to authorise measurements.
/// </summary>
public sealed class SensorRegistry : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public SensorRegistry(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>
    /// Ensures the <c>sensors</c> table exists. Idempotent.
    /// Call once on Gateway boot, before consuming starts.
    /// </summary>
    public async Task InitSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _dataSource.CreateCommand(SensorsSchema.CreateTable);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// INSERTs or UPDATEs the row for <paramref name="sensorId"/>, refreshing
    /// its zone, status, and last_seen timestamp.
    /// </summary>
    public async Task UpsertAsync(
        uint sensorId,
        string zone,
        string status,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = _dataSource.CreateCommand(SensorsSchema.UpsertSensor);
        cmd.Parameters.AddWithValue((int)sensorId);
        cmd.Parameters.AddWithValue(zone);
        cmd.Parameters.AddWithValue(status);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
