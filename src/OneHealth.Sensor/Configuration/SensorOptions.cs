namespace OneHealth.Sensor.Configuration;

/// <summary>
/// Parsed command-line options for a sensor instance.
/// Created via <see cref="Parse"/>, which validates the inputs and
/// resolves the hardcoded sensor → zone mapping.
/// </summary>
public class SensorOptions
{
    public uint SensorId { get; }
    public string Zone { get; }
    public SensorMode Mode { get; }

    private SensorOptions(uint sensorId, string zone, SensorMode mode)
    {
        SensorId = sensorId;
        Zone = zone;
        Mode = mode;
    }

    /// <summary>
    /// Parses command-line arguments. Expected format:
    /// <c>&lt;sensor_id&gt; &lt;mode&gt;</c>. Example: <c>101 auto</c>.
    /// </summary>
    /// <exception cref="ArgumentException">If args are missing or invalid.</exception>
    public static SensorOptions Parse(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException(
                "Usage: dotnet run -- <sensor_id> <mode>\n" +
                "  sensor_id: 101, 102, 103 or 104\n" +
                "  mode: auto | manual");

        if (!uint.TryParse(args[0], out var sensorId))
            throw new ArgumentException(
                $"Invalid sensor_id '{args[0]}' — must be a positive integer.");

        var mode = args[1].ToLowerInvariant() switch
        {
            "auto"   => SensorMode.Auto,
            "manual" => SensorMode.Manual,
            _ => throw new ArgumentException(
                $"Invalid mode '{args[1]}' — must be 'auto' or 'manual'.")
        };

        var zone = ResolveZone(sensorId);
        return new SensorOptions(sensorId, zone, mode);
    }

    /// <summary>
    /// Hardcoded sensor → zone mapping. In a real system this would come
    /// from a registry; for the academic project we keep it simple.
    /// </summary>
    private static string ResolveZone(uint sensorId) => sensorId switch
    {
        101 or 102 => "ZONE_NORTH",
        103 or 104 => "ZONE_SOUTH",
        999 => "ZONE_NORTH",   // ad-hoc "rogue sensor" id for manual unauthorized-flow tests
        _ => throw new ArgumentException(
            $"Unknown sensor_id {sensorId} — no zone configured. " +
            "Add it to SensorOptions.ResolveZone() to register a new sensor.")
    };
}

public enum SensorMode
{
    Auto,
    Manual
}