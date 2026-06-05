using System.Globalization;

namespace OneHealth.Gateway.Configuration;

/// <summary>
/// Parsed command-line and configuration-file options for a gateway instance.
///
/// A gateway is identified by its port number (5001, 5002, …). At boot it
/// reads <c>data/gateway_configs/gw_&lt;port&gt;.csv</c> to learn:
///   • which sensors it is responsible for (and is authorised to accept), and
///   • which zones to subscribe to on the RabbitMQ topic exchange.
///
/// Zones are derived from the distinct values in the CSV's <c>zone</c> column,
/// so the subscription pattern adapts automatically when new sensors are added.
/// </summary>
public class GatewayOptions
{
    public int Port { get; }
    public IReadOnlyList<string> Zones { get; }
    public IReadOnlyDictionary<uint, SensorEntry> AllowedSensors { get; }

    private GatewayOptions(
        int port,
        IReadOnlyList<string> zones,
        IReadOnlyDictionary<uint, SensorEntry> allowedSensors)
    {
        Port = port;
        Zones = zones;
        AllowedSensors = allowedSensors;
    }

    /// <summary>
    /// Parses CLI args (<c>&lt;port&gt;</c>) and the matching config CSV.
    /// </summary>
    /// <exception cref="ArgumentException">If args are missing or invalid.</exception>
    /// <exception cref="FileNotFoundException">If the config CSV is missing.</exception>
    /// <exception cref="InvalidDataException">If the config CSV is malformed.</exception>
    public static GatewayOptions Parse(string[] args, string configDirectory)
    {
        if (args.Length < 1)
            throw new ArgumentException(
                "Usage: dotnet run -- <port>\n" +
                "  port: gateway port id (e.g. 5001, 5002)");

        if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            throw new ArgumentException(
                $"Invalid port '{args[0]}' — must be a positive integer.");

        var configFile = Path.Combine(configDirectory, $"gw_{port}.csv");
        if (!File.Exists(configFile))
            throw new FileNotFoundException(
                $"Gateway config file not found: {configFile}", configFile);

        var entries = LoadConfig(configFile);

        var zones = entries.Values
            .Select(e => e.Zone)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (zones.Count == 0)
            throw new InvalidDataException(
                $"Config '{configFile}' yields zero zones — at least one sensor row is required.");

        return new GatewayOptions(port, zones, entries);
    }

    private static IReadOnlyDictionary<uint, SensorEntry> LoadConfig(string path)
    {
        var dict = new Dictionary<uint, SensorEntry>();
        var lineNumber = 0;

        foreach (var raw in File.ReadLines(path))
        {
            lineNumber++;
            if (lineNumber == 1) continue; // header

            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split(';');
            if (parts.Length < 3)
                throw new InvalidDataException(
                    $"Line {lineNumber} of '{path}': expected at least 3 fields, got {parts.Length}.");

            if (!uint.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sensorId))
                throw new InvalidDataException(
                    $"Line {lineNumber} of '{path}': sensor_id '{parts[0]}' is not a valid integer.");

            var status = parts[1].Trim();
            var zone = parts[2].Trim();

            if (zone.Length == 0)
                throw new InvalidDataException(
                    $"Line {lineNumber} of '{path}': zone must not be empty.");

            dict[sensorId] = new SensorEntry(sensorId, status, zone);
        }

        return dict;
    }
}

/// <summary>
/// One row of the gateway configuration CSV.
/// </summary>
public record SensorEntry(uint SensorId, string Status, string Zone);
