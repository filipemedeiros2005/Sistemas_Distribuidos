using OneHealth.Common;

namespace OneHealth.Sensor.Detection;

/// <summary>
/// Classifies a measurement as <see cref="MsgType.Data"/> (normal) or
/// <see cref="MsgType.Alert"/> based on hard limits per data type.
///
/// Hard limits are tight enough that the sample CSV produces a healthy
/// mix of both outcomes — values inside the (min, max) range are Data,
/// outside are Alert. Unknown data types default to Data, since the
/// upstream pre-processor will validate physical bounds anyway.
/// </summary>
public class AnomalyClassifier
{
    /// <summary>
    /// Default (min, max) inclusive ranges per data type.
    /// Editable from outside the class via the constructor overload that
    /// accepts a custom dictionary, for testing or specialised sensors.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (double Min, double Max)> DefaultLimits =
        new Dictionary<string, (double Min, double Max)>
        {
            ["TEMP"]  = (-20.0, 35.0),
            ["HUM"]   = (5.0, 95.0),
            ["PM25"]  = (0.0, 75.0),
            ["PM10"]  = (0.0, 150.0),
            ["NOISE"] = (0.0, 110.0),
            ["LUM"]   = (0.0, 100_000.0)
        };

    private readonly IReadOnlyDictionary<string, (double Min, double Max)> _limits;

    public AnomalyClassifier() : this(DefaultLimits) { }

    public AnomalyClassifier(IReadOnlyDictionary<string, (double Min, double Max)> limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    /// <summary>
    /// Returns <see cref="MsgType.Alert"/> if <paramref name="value"/> falls
    /// outside the configured range for <paramref name="dataType"/>;
    /// <see cref="MsgType.Data"/> otherwise.
    /// </summary>
    public MsgType Classify(string dataType, double value)
    {
        if (!_limits.TryGetValue(dataType, out var range))
            return MsgType.Data;

        return (value < range.Min || value > range.Max)
            ? MsgType.Alert
            : MsgType.Data;
    }
}