using System.Globalization;

namespace OneHealth.Server.Coordinator;

/// <summary>
/// Parses the pipe-delimited request string the Dashboard sends over TCP.
///
/// Grammar: <c>KEY=value(|KEY=value)*</c>, where keys are case-insensitive
/// and any unknown key is ignored. Examples:
/// <code>
///   PING
///   KIND=AVG|WINDOW=60
///   KIND=FORECAST|ZONA=ZONE_NORTH|TYPES=TEMP,HUM|HORIZON=10
///   KIND=ANOMALY_RATE|SENSORS=101,102|WINDOW=120
/// </code>
///
/// The special single-token request <c>PING</c> is accepted and returned
/// from <see cref="TryParseControl"/> — used by the Dashboard mini-spike
/// to check end-to-end connectivity without depending on Python.
/// </summary>
public static class AnalysisQueryParser
{
    /// <summary>
    /// Returns true if the raw request is a control command (e.g. "PING").
    /// </summary>
    public static bool TryParseControl(string raw, out string command)
    {
        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "PING", StringComparison.OrdinalIgnoreCase))
        {
            command = "PING";
            return true;
        }
        command = string.Empty;
        return false;
    }

    /// <exception cref="FormatException">If the request is malformed.</exception>
    public static AnalysisQuery Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Request must not be empty.");

        var fields = raw.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            var idx = field.IndexOf('=');
            if (idx <= 0)
                throw new FormatException($"Malformed field '{field}' — expected KEY=VALUE.");

            var key = field[..idx].Trim();
            var value = field[(idx + 1)..].Trim();
            bag[key] = value;
        }

        if (!bag.TryGetValue("KIND", out var kind) || string.IsNullOrWhiteSpace(kind))
            throw new FormatException("KIND is required.");

        var window = bag.TryGetValue("WINDOW", out var w) && int.TryParse(w, CultureInfo.InvariantCulture, out var winMin)
            ? winMin
            : 60;

        var types = bag.TryGetValue("TYPES", out var ts)
            ? ts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        IReadOnlyList<uint> sensorIds = Array.Empty<uint>();
        if (bag.TryGetValue("SENSORS", out var sn))
        {
            sensorIds = sn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Select(s => uint.Parse(s, CultureInfo.InvariantCulture))
                          .ToArray();
        }

        var zone = bag.TryGetValue("ZONA", out var z) ? z : null;
        if (zone is not null && zone.Length == 0) zone = null;

        int? horizon = null;
        if (bag.TryGetValue("HORIZON", out var h) &&
            int.TryParse(h, CultureInfo.InvariantCulture, out var hValue))
        {
            horizon = hValue;
        }

        return new AnalysisQuery
        {
            Kind              = kind.ToUpperInvariant(),
            WindowMinutes     = window,
            DataTypes         = types,
            ExplicitSensorIds = sensorIds,
            Zone              = zone,
            Horizon           = horizon
        };
    }
}
