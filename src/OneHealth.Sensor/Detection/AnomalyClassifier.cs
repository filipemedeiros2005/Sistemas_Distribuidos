using OneHealth.Common;

namespace OneHealth.Sensor.Detection;

/// <summary>
/// Classifies a measurement as <see cref="MsgType.Data"/> (normal) or
/// <see cref="MsgType.Alert"/> based on the per-type "normal" range defined
/// once in <see cref="MeasurementLimits.Normal"/>. A value inside the range is
/// Data; outside is an Alert. Unknown data types default to Data, since the
/// upstream pre-processor still validates physical plausibility.
/// </summary>
public class AnomalyClassifier
{
    private readonly IReadOnlyDictionary<string, MeasurementLimits.Range> _limits;

    public AnomalyClassifier() : this(MeasurementLimits.Normal) { }

    public AnomalyClassifier(IReadOnlyDictionary<string, MeasurementLimits.Range> limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    /// <summary>
    /// Returns <see cref="MsgType.Alert"/> if <paramref name="value"/> falls
    /// outside the configured range for <paramref name="dataType"/>;
    /// <see cref="MsgType.Data"/> otherwise.
    /// </summary>
    public MsgType Classify(string dataType, double value) =>
        _limits.TryGetValue(dataType, out var range) && !range.Contains(value)
            ? MsgType.Alert
            : MsgType.Data;
}