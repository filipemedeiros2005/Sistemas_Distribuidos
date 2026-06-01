namespace OneHealth.Common;

/// <summary>
/// Per-data-type value limits, in canonical units, kept in one place so the
/// Sensor and the Pre-processor cannot drift apart. Two nested bands:
///
/// <list type="bullet">
///   <item><b>Normal</b> — the everyday operating range. A reading outside it
///   is flagged as an <i>anomaly</i> (the Sensor turns it into an ALERT) but is
///   still a real, kept measurement.</item>
///   <item><b>Plausible</b> — the widest physically meaningful range. A reading
///   outside it is treated as corrupt/impossible and the Pre-processor
///   <i>drops</i> it.</item>
/// </list>
///
/// By construction <c>Plausible ⊇ Normal</c>: every value is first "normal",
/// then "anomalous but plausible", then "implausible". Luminosity is the one
/// that surprises people — daylight runs into the tens of thousands of lux and
/// direct summer sun approaches 100 000 lux, so a 25 000 lux reading is normal,
/// not an anomaly.
/// </summary>
public static class MeasurementLimits
{
    public readonly record struct Range(double Min, double Max)
    {
        public bool Contains(double value) => value >= Min && value <= Max;
    }

    /// <summary>Everyday range. Outside ⇒ anomaly (ALERT), still kept.</summary>
    public static readonly IReadOnlyDictionary<string, Range> Normal =
        new Dictionary<string, Range>
        {
            ["TEMP"]  = new(-20.0, 35.0),
            ["HUM"]   = new(5.0, 95.0),
            ["PM25"]  = new(0.0, 75.0),
            ["PM10"]  = new(0.0, 150.0),
            ["NOISE"] = new(0.0, 110.0),
            ["LUM"]   = new(0.0, 100_000.0),
        };

    /// <summary>Widest physically plausible range. Outside ⇒ dropped as corrupt.</summary>
    public static readonly IReadOnlyDictionary<string, Range> Plausible =
        new Dictionary<string, Range>
        {
            ["TEMP"]  = new(-40.0, 70.0),
            ["HUM"]   = new(0.0, 100.0),
            ["PM25"]  = new(0.0, 1000.0),
            ["PM10"]  = new(0.0, 1000.0),
            ["NOISE"] = new(0.0, 140.0),
            ["LUM"]   = new(0.0, 150_000.0),
        };
}
