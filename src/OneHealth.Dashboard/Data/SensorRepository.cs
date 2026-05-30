using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace OneHealth.Dashboard.Data;

/// <summary>
/// Read-only access for the Sensors tab. Pulls the sensor registry — the
/// rows the Gateway UPSERTs on Hello/Status/Bye — so the Dashboard can show
/// who is online or offline.
/// </summary>
public sealed class SensorRepository : IAsyncDisposable
{
    private const string SelectAll = @"
        SELECT sensor_id, zone, status, last_seen
        FROM sensors
        ORDER BY sensor_id;";

    private readonly NpgsqlDataSource _dataSource;

    public SensorRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<List<SensorRow>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<SensorRow>();
        await using var cmd = _dataSource.CreateCommand(SelectAll);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SensorRow
            {
                SensorId = reader.GetInt32(0),
                Zone     = reader.GetString(1),
                Status   = reader.GetString(2),
                LastSeen = reader.GetDateTime(3),
            });
        }
        return rows;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

/// <summary>
/// One row of the sensor registry, shaped for the Sensors DataGrid.
/// </summary>
public sealed class SensorRow
{
    public int SensorId { get; init; }
    public string Zone { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime LastSeen { get; init; }

    public bool IsOnline =>
        string.Equals(Status, "ONLINE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Formatted local time of the sensor's last contact.</summary>
    public string LastSeenText =>
        LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
