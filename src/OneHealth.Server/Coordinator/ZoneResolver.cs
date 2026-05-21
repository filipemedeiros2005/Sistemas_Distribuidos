using Npgsql;

namespace OneHealth.Server.Coordinator;

/// <summary>
/// Translates a zone name (e.g. "ZONE_NORTH") into the list of sensor ids
/// belonging to it, consulting the <c>sensors</c> table populated by the
/// Gateway. Lives in the Server so the Python analysis service stays
/// zone-blind — it only ever sees sensor ids.
/// </summary>
public sealed class ZoneResolver : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public ZoneResolver(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<IReadOnlyList<uint>> ResolveAsync(string zone, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zone))
            return Array.Empty<uint>();

        await using var cmd = _dataSource.CreateCommand("SELECT sensor_id FROM sensors WHERE zone = $1");
        cmd.Parameters.AddWithValue(zone);

        var result = new List<uint>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add((uint)reader.GetInt32(0));

        return result;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
