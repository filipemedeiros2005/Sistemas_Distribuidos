namespace OneHealth.Common.Db;

/// <summary>
/// SQL DDL for the <c>sensors</c> table — the single registry of which
/// sensors the system knows about. Gateway UPSERTs into it on Hello/Status;
/// Preprocessor reads from it to authorise incoming measurements.
///
/// Centralised here so both projects use exactly the same definition.
/// Idempotent (<c>CREATE TABLE IF NOT EXISTS</c>) so it is safe to invoke
/// on every cold start, in any order.
/// </summary>
public static class SensorsSchema
{
    public const string CreateTable = @"
        CREATE TABLE IF NOT EXISTS sensors (
            sensor_id  INTEGER     PRIMARY KEY,
            zone       VARCHAR(50) NOT NULL,
            status     VARCHAR(20) NOT NULL DEFAULT 'ONLINE',
            last_seen  TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );";

    /// <summary>
    /// UPSERT performed by the Gateway whenever it receives a Hello or Status
    /// packet from a sensor. Inserts the row on first contact; otherwise
    /// refreshes <c>status</c> and <c>last_seen</c>.
    /// </summary>
    public const string UpsertSensor = @"
        INSERT INTO sensors (sensor_id, zone, status, last_seen)
        VALUES ($1, $2, $3, NOW())
        ON CONFLICT (sensor_id) DO UPDATE
            SET status    = EXCLUDED.status,
                last_seen = NOW();";

    /// <summary>
    /// Authorization probe used by the Preprocessor. Returns 1 row iff the
    /// sensor is registered.
    /// </summary>
    public const string IsSensorAuthorized = @"
        SELECT 1 FROM sensors WHERE sensor_id = $1;";

    /// <summary>
    /// Pre-registers a known sensor as OFFLINE on gateway boot, so the
    /// Dashboard's sensor view lists it before it ever connects. Does nothing
    /// if the row already exists — a sensor that is already ONLINE keeps its
    /// state and last_seen.
    /// </summary>
    public const string PreregisterSensor = @"
        INSERT INTO sensors (sensor_id, zone, status, last_seen)
        VALUES ($1, $2, 'OFFLINE', NOW())
        ON CONFLICT (sensor_id) DO NOTHING;";
}
