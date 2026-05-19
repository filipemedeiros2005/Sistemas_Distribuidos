using OneHealth.Sensor.Configuration;
using OneHealth.Sensor.Csv;

try
{
    var options = SensorOptions.Parse(args);
    Console.WriteLine(
        $"[BOOT] Sensor {options.SensorId} | zone={options.Zone} | mode={options.Mode}");

    var csvPath = ResolveSimulationCsvPath(options.SensorId);
    Console.WriteLine($"[BOOT] Reading from {csvPath}");

    var reader = new CsvSimulationReader(csvPath);

    // Wire Ctrl+C → cancel the read loop gracefully.
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;          // prevent the runtime from killing us instantly
        cts.Cancel();             // signal our loop to stop
        Console.WriteLine("\n[SHUTDOWN] Ctrl+C received, stopping...");
    };

    var count = 0;
    try
    {
        await foreach (var reading in reader.ReadLoopAsync(cts.Token))
        {
            count++;
            Console.WriteLine(
                $"[READ #{count:D4}] {reading.DataType,-6} = {reading.Value,8:F2}   (delay {reading.DelayMs,4}ms)");

            // Smoke-test cap — will be removed in checkpoint 2.J once the
            // publisher is wired in and the sensor is meant to run forever.
            if (count >= 30) { cts.Cancel(); break; }
        }
    }
    catch (OperationCanceledException) { /* expected on Ctrl+C */ }

    Console.WriteLine($"[SHUTDOWN] Read {count} readings total. Goodbye.");
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.Message}");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FATAL] {ex.GetType().Name}: {ex.Message}");
    Environment.Exit(2);
}

// --- helpers ---------------------------------------------------------------

static string ResolveSimulationCsvPath(uint sensorId)
{
    // Walk up from the current directory until OneHealth.sln is found.
    // This makes the path resolution work whether the binary is launched
    // via `dotnet run --project src/OneHealth.Sensor` (cwd = csproj dir)
    // or from the repo root, or even from a published artifact.
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OneHealth.sln")))
        dir = dir.Parent;

    if (dir == null)
        throw new InvalidOperationException(
            "Cannot locate repo root (OneHealth.sln not found in current dir or parents).");

    return Path.Combine(dir.FullName, "data", "simulation", $"sensor_{sensorId}.csv");
}