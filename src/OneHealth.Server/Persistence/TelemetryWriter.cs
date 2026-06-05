using Npgsql;
using OneHealth.Common;
using OneHealth.Common.Db;

namespace OneHealth.Server.Persistence;

/// <summary>
/// Persists telemetry packets into the PostgreSQL <c>telemetry</c> table.
/// Owns a single <see cref="NpgsqlDataSource"/> for the Server's lifetime —
/// internal pooling amortises connection cost across thousands of inserts.
///
/// The <c>is_anomaly</c> column is derived from the packet's
/// <see cref="MsgType"/>: only <see cref="MsgType.Alert"/> sets it to true,
/// so downstream analysis can compute anomaly rates with a simple filter.
/// </summary>
public sealed class TelemetryWriter : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public TelemetryWriter(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>Ensures the table and indexes exist. Idempotent.</summary>
    public async Task InitSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = _dataSource.CreateCommand(TelemetrySchema.CreateTable);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Inserts one row.</summary>
    public async Task InsertAsync(TelemetryPacket packet, CancellationToken cancellationToken = default)
    {
        await using var cmd = _dataSource.CreateCommand(TelemetrySchema.InsertMeasurement);
        cmd.Parameters.AddWithValue((int)packet.SensorId);
        cmd.Parameters.AddWithValue(DataTypeMapping.ToName(packet.DataType));
        cmd.Parameters.AddWithValue((double)packet.Value);
        cmd.Parameters.AddWithValue(packet.Timestamp);
        cmd.Parameters.AddWithValue(packet.MsgType == MsgType.Alert);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
