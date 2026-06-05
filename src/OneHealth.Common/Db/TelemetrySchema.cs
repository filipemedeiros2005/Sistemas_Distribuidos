namespace OneHealth.Common.Db;

/// <summary>
/// SQL DDL/DML for the <c>telemetry</c> table — the time-series store that
/// the Server persists every Data/Alert measurement into. Indexed for the
/// time-window queries the Analysis service will issue on Day 5.
/// Idempotent so the Server can call it on every cold start.
/// </summary>
public static class TelemetrySchema
{
    public const string CreateTable = @"
        CREATE TABLE IF NOT EXISTS telemetry (
            id          BIGSERIAL PRIMARY KEY,
            sensor_id   INTEGER          NOT NULL,
            data_type   VARCHAR(20)      NOT NULL,
            value       DOUBLE PRECISION NOT NULL,
            unix_ts     BIGINT           NOT NULL,
            is_anomaly  BOOLEAN          NOT NULL DEFAULT FALSE
        );
        CREATE INDEX IF NOT EXISTS idx_telemetry_unix_ts
            ON telemetry (unix_ts DESC);
        CREATE INDEX IF NOT EXISTS idx_telemetry_sensor_ts
            ON telemetry (sensor_id, unix_ts DESC);";

    /// <summary>
    /// Single-row insert. Parameters in order: sensor_id, data_type, value,
    /// unix_ts, is_anomaly.
    /// </summary>
    public const string InsertMeasurement = @"
        INSERT INTO telemetry (sensor_id, data_type, value, unix_ts, is_anomaly)
        VALUES ($1, $2, $3, $4, $5);";
}
