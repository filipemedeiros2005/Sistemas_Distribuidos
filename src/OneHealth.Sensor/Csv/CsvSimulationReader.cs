using System.Globalization;
using System.Runtime.CompilerServices;

namespace OneHealth.Sensor.Csv;

/// <summary>
/// Reads simulated sensor measurements from a CSV file asynchronously.
/// When the end of file is reached, the reader loops back to the beginning,
/// producing an indefinite stream — that is what a real sensor running
/// continuously looks like. Honours <see cref="SimulatedReading.DelayMs"/>
/// between emissions to simulate real-time pacing.
/// </summary>
public class CsvSimulationReader
{
    private readonly string _filePath;

    public CsvSimulationReader(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        _filePath = filePath;
    }

    /// <summary>
    /// Yields one reading at a time. Each row's <c>delay_ms</c> is awaited
    /// before emitting the value, so the consumer sees data arrive at the
    /// pace declared in the CSV. Stops cleanly on <see cref="OperationCanceledException"/>.
    /// </summary>
    public async IAsyncEnumerable<SimulatedReading> ReadLoopAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            throw new FileNotFoundException(
                $"Simulation CSV not found at '{_filePath}'.", _filePath);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream);

            // First row is the header — discard.
            await reader.ReadLineAsync(cancellationToken);
            var lineNumber = 1;

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                lineNumber++;
                line = line.Trim();

                // Allow blank lines and shell-style '#' comments for human-edited CSVs.
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var reading = ParseRow(line, lineNumber);

                // Honour the row's delay before emitting — simulates real time.
                await Task.Delay(reading.DelayMs, cancellationToken);

                yield return reading;
            }
            // End of file reached — the outer while restarts the StreamReader from the top.
        }
    }

    private SimulatedReading ParseRow(string line, int lineNumber)
    {
        var parts = line.Split(';');
        if (parts.Length != 3)
            throw new InvalidDataException(
                $"Line {lineNumber} of '{_filePath}': expected 3 fields separated by ';', got {parts.Length}.");

        var dataType = parts[0].Trim();
        if (dataType.Length == 0)
            throw new InvalidDataException(
                $"Line {lineNumber} of '{_filePath}': data_type must not be empty.");

        if (!double.TryParse(parts[1].Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException(
                $"Line {lineNumber} of '{_filePath}': value '{parts[1]}' is not a valid number.");

        if (!int.TryParse(parts[2].Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var delayMs))
            throw new InvalidDataException(
                $"Line {lineNumber} of '{_filePath}': delay_ms '{parts[2]}' is not a valid integer.");

        if (delayMs < 0)
            throw new InvalidDataException(
                $"Line {lineNumber} of '{_filePath}': delay_ms must be non-negative, got {delayMs}.");

        return new SimulatedReading(dataType, value, delayMs);
    }
}

/// <summary>
/// A single measurement read from the simulation CSV.
/// Immutable; constructed by <see cref="CsvSimulationReader"/>.
/// </summary>
public record SimulatedReading(string DataType, double Value, int DelayMs);