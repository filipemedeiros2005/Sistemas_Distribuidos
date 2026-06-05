namespace OneHealth.Server.Coordinator;

/// <summary>
/// Parsed representation of the pipe-delimited string the Dashboard sends
/// to the AnalysisCoordinator. Decoupled from the gRPC <c>AnalysisRequest</c>
/// so the wire format used between Dashboard ↔ Server can evolve
/// independently from the Server ↔ Python contract.
/// </summary>
public sealed class AnalysisQuery
{
    /// <summary>Analysis kind: "AVG", "STDDEV", "ANOMALY_RATE", "FORECAST".</summary>
    public required string Kind { get; init; }

    /// <summary>Window size in minutes (default 60).</summary>
    public int WindowMinutes { get; init; } = 60;

    /// <summary>Data types to include. Empty list = no filter (all types).</summary>
    public IReadOnlyList<string> DataTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Explicit sensor ids. If non-empty, takes precedence over <see cref="Zone"/>.
    /// </summary>
    public IReadOnlyList<uint> ExplicitSensorIds { get; init; } = Array.Empty<uint>();

    /// <summary>Zone filter. Resolved to sensor ids by the coordinator.</summary>
    public string? Zone { get; init; }

    /// <summary>FORECAST horizon (points ahead). Null = analysis default.</summary>
    public int? Horizon { get; init; }
}
