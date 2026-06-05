using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace OneHealth.Dashboard.Data;

/// <summary>
/// Read-only access for the live Telemetry tab. Pulls the most recent rows
/// from the <c>telemetry</c> table on every Dashboard refresh tick.
/// </summary>
public sealed class TelemetryRepository : IAsyncDisposable
{
    private const string SelectRecent = @"
        SELECT id, sensor_id, data_type, value, unix_ts, is_anomaly
        FROM telemetry
        ORDER BY unix_ts DESC, id DESC
        LIMIT $1;";

    private readonly NpgsqlDataSource _dataSource;

    public TelemetryRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<List<TelemetryRow>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var rows = new List<TelemetryRow>(limit);
        await using var cmd = _dataSource.CreateCommand(SelectRecent);
        cmd.Parameters.AddWithValue(limit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new TelemetryRow
            {
                Id        = reader.GetInt64(0),
                SensorId  = reader.GetInt32(1),
                DataType  = reader.GetString(2),
                Value     = reader.GetDouble(3),
                UnixTs    = reader.GetInt64(4),
                IsAnomaly = reader.GetBoolean(5),
            });
        }
        return rows;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

/// <summary>
/// One row of the live telemetry feed. Exposes <see cref="Timestamp"/> for
/// the DataGrid (formatted) while keeping the raw <see cref="UnixTs"/> for
/// debugging.
/// </summary>
public sealed class TelemetryRow
{
    public long Id { get; init; }
    public int SensorId { get; init; }
    public string DataType { get; init; } = string.Empty;
    public double Value { get; init; }
    public long UnixTs { get; init; }
    public bool IsAnomaly { get; init; }

    /// <summary>Formatted local time, used as a DataGrid column.</summary>
    public string Timestamp =>
        DateTimeOffset.FromUnixTimeMilliseconds(UnixTs).ToLocalTime().ToString("HH:mm:ss.fff");
}
