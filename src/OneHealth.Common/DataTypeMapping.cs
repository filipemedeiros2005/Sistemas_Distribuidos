namespace OneHealth.Common;

/// <summary>
/// Bidirectional mapping between short uppercase data-type names used in
/// CSV files and routing keys ("TEMP", "HUM", …) and the strongly-typed
/// <see cref="DataType"/> enum carried in the binary packet.
///
/// Centralised here so the Sensor, Gateway, and Server agree on the exact
/// spelling — a typo in any one of them would silently break routing.
/// </summary>
public static class DataTypeMapping
{
    private static readonly Dictionary<string, DataType> ByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["TEMP"]  = DataType.Temperature,
            ["HUM"]   = DataType.Humidity,
            ["PM25"]  = DataType.Pm25,
            ["PM10"]  = DataType.Pm10,
            ["NOISE"] = DataType.Noise,
            ["LUM"]   = DataType.Luminosity,
            ["HEARTBEAT"] = DataType.Heartbeat
        };

    private static readonly Dictionary<DataType, string> ByValue =
        new()
        {
            [DataType.Temperature] = "TEMP",
            [DataType.Humidity]    = "HUM",
            [DataType.Pm25]        = "PM25",
            [DataType.Pm10]        = "PM10",
            [DataType.Noise]       = "NOISE",
            [DataType.Luminosity]  = "LUM",
            [DataType.Heartbeat]   = "HEARTBEAT"
        };

    /// <summary>
    /// Resolves a CSV / routing-key name into the corresponding <see cref="DataType"/>.
    /// Case-insensitive.
    /// </summary>
    /// <exception cref="ArgumentException">If the name is not recognised.</exception>
    public static DataType FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name must not be empty.", nameof(name));

        if (ByName.TryGetValue(name, out var type)) return type;

        throw new ArgumentException(
            $"Unknown data type name '{name}'. " +
            $"Valid names: {string.Join(", ", ByName.Keys)}", nameof(name));
    }

    /// <summary>Returns the canonical short name ("TEMP", "HUM", …) for an enum value.</summary>
    public static string ToName(DataType type) =>
        ByValue.TryGetValue(type, out var name)
            ? name
            : type.ToString().ToUpperInvariant();

    /// <summary>
    /// Real measurement type names (everything except the HEARTBEAT envelope).
    /// Used, for example, to tell the user which types are accepted in the
    /// sensor's manual input mode.
    /// </summary>
    public static IReadOnlyList<string> MeasurementNames { get; } =
        ByName.Keys.Where(n => n != "HEARTBEAT").ToArray();
}