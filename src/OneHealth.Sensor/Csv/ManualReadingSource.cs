using System.Globalization;
using System.Runtime.CompilerServices;
using OneHealth.Common;

namespace OneHealth.Sensor.Csv;

/// <summary>
/// Yields measurements typed by a human at the terminal, in the same
/// <see cref="SimulatedReading"/> shape the CSV reader produces. This is the
/// "manual" sensor mode: the operator enters readings by hand
/// (<c>&lt;TYPE&gt; &lt;value&gt;</c>, e.g. <c>TEMP 25.5</c>) and they travel
/// through the exact same path as automatic readings — broker, gateway,
/// pre-processor RPC, aggregated exchange, server and dashboard.
/// </summary>
public static class ManualReadingSource
{
    public static async IAsyncEnumerable<SimulatedReading> ReadLoopAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");

            // Console.ReadLine blocks; run it on a worker thread so Ctrl+C /
            // SIGTERM cancellation is honoured instead of hanging on input.
            string? line;
            try
            {
                line = await Task.Run(Console.ReadLine, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (line is null) yield break;          // EOF (e.g. piped input ended)

            line = line.Trim();
            if (line.Length == 0) continue;

            if (!TryParse(line, out var reading))
            {
                Console.WriteLine(
                    $"[INPUT] Invalid. Use '<TYPE> <value>', e.g. 'TEMP 25.5'. " +
                    $"Types: {string.Join(", ", DataTypeMapping.MeasurementNames)}");
                continue;
            }

            yield return reading;
        }
    }

    private static bool TryParse(string line, out SimulatedReading reading)
    {
        reading = null!;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        var type = parts[0].ToUpperInvariant();
        if (!DataTypeMapping.MeasurementNames.Contains(type)) return false;

        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        // No artificial delay — the reading is emitted as soon as it is typed.
        reading = new SimulatedReading(type, value, 0);
        return true;
    }
}
