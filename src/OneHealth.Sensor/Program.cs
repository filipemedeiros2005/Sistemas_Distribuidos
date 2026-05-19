using System.Globalization;
using OneHealth.Common;
using OneHealth.Sensor.Configuration;
using OneHealth.Sensor.Csv;
using OneHealth.Sensor.Detection;
using OneHealth.Sensor.Publishing;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

try
{
    var options = SensorOptions.Parse(args);
    Console.WriteLine(
        $"[BOOT] Sensor {options.SensorId} | zone={options.Zone} | mode={options.Mode}");

    var csvPath = ResolveSimulationCsvPath(options.SensorId);
    Console.WriteLine($"[BOOT] Reading from {csvPath}");

    var reader     = new CsvSimulationReader(csvPath);
    var classifier = new AnomalyClassifier();

    await using var publisher = new RabbitMqPublisher(options.Zone, options.SensorId);
    Console.WriteLine("[BOOT] Connecting to RabbitMQ at localhost:5672...");
    await publisher.ConnectAsync();
    Console.WriteLine("[BOOT] Connected.");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\n[SHUTDOWN] Ctrl+C received, stopping...");
    };

    // HELLO — announces the sensor is alive. Payload value is unused.
    await publisher.PublishAsync(BuildEnvelopePacket(options.SensorId, MsgType.Hello));
    Console.WriteLine("[PUB ] HELLO sent.");

    var count = 0;
    try
    {
        await foreach (var reading in reader.ReadLoopAsync(cts.Token))
        {
            count++;
            var msgType  = classifier.Classify(reading.DataType, reading.Value);
            var dataType = DataTypeMapping.FromName(reading.DataType);

            var packet = new TelemetryPacket
            {
                SensorId  = options.SensorId,
                MsgType   = msgType,
                DataType  = dataType,
                Value     = (float)reading.Value,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await publisher.PublishAsync(packet, cts.Token);

            Console.WriteLine(
                $"[PUB #{count:D4}] {msgType,-5} {reading.DataType,-6} = {reading.Value,9:F2}   (delay {reading.DelayMs,4}ms)");

            // Smoke-test cap; removed in checkpoint 2.J when sensor runs indefinitely.
            if (count >= 30) { cts.Cancel(); break; }
        }
    }
    catch (OperationCanceledException) { /* expected */ }

    // BYE — graceful farewell, even if the loop ended abruptly.
    try
    {
        await publisher.PublishAsync(BuildEnvelopePacket(options.SensorId, MsgType.Bye));
        Console.WriteLine("[PUB ] BYE sent.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[WARN] BYE publish failed: {ex.Message}");
    }

    Console.WriteLine($"[SHUTDOWN] Published {count} readings. Goodbye.");
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
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OneHealth.sln")))
        dir = dir.Parent;

    if (dir == null)
        throw new InvalidOperationException(
            "Cannot locate repo root (OneHealth.sln not found in current dir or parents).");

    return Path.Combine(dir.FullName, "data", "simulation", $"sensor_{sensorId}.csv");
}

static TelemetryPacket BuildEnvelopePacket(uint sensorId, MsgType msgType) =>
    new()
    {
        SensorId  = sensorId,
        MsgType   = msgType,
        DataType  = DataType.Heartbeat,   // not a real measurement
        Value     = 0f,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };