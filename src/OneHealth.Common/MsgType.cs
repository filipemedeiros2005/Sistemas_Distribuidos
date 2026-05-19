namespace OneHealth.Common;

/// <summary>
/// One-byte message type carried in the binary telemetry packet header.
/// </summary>
public enum MsgType : byte
{
    /// <summary>Sensor handshake / first contact with the system.</summary>
    Hello = 0,

    /// <summary>Normal measurement; eligible for pre-processing.</summary>
    Data = 1,

    /// <summary>Anomalous measurement; bypasses pre-processing (see design rule 2.5).</summary>
    Alert = 2,

    /// <summary>Periodic heartbeat from a sensor; used by gateway watchdog.</summary>
    Status = 3,

    /// <summary>Graceful shutdown notification from a sensor.</summary>
    Bye = 4
}