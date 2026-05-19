using System.Globalization;
using OneHealth.Gateway.Configuration;
using OneHealth.Gateway.Consuming;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

try
{
    var configDir = ResolveGatewayConfigDir();
    var options = GatewayOptions.Parse(args, configDir);

    Console.WriteLine(
        $"[BOOT] Gateway port={options.Port} | zones=[{string.Join(", ", options.Zones)}] | " +
        $"sensors={options.AllowedSensors.Count}");

    await using var consumer = new RabbitMqConsumer(options.Port, options.Zones);
    Console.WriteLine("[BOOT] Connecting to RabbitMQ at localhost:5672...");
    await consumer.ConnectAsync();
    Console.WriteLine("[BOOT] Connected. Listening for telemetry...");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\n[SHUTDOWN] Ctrl+C received, stopping...");
    };

    using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
        System.Runtime.InteropServices.PosixSignal.SIGTERM,
        ctx =>
        {
            ctx.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\n[SHUTDOWN] SIGTERM received, stopping...");
        });

    var count = 0;
    await consumer.ConsumeAsync((packet, routingKey) =>
    {
        count++;
        Console.WriteLine(
            $"[RECV #{count:D4}] {packet.MsgType,-6} {packet.DataType,-11} " +
            $"sid={packet.SensorId,-3} val={packet.Value,9:F2}   key={routingKey}");
        return Task.CompletedTask;
    }, cts.Token);

    Console.WriteLine($"[SHUTDOWN] Received {count} messages total. Goodbye.");
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.Message}");
    Environment.Exit(1);
}
catch (FileNotFoundException ex)
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

static string ResolveGatewayConfigDir()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OneHealth.sln")))
        dir = dir.Parent;

    if (dir == null)
        throw new InvalidOperationException(
            "Cannot locate repo root (OneHealth.sln not found in current dir or parents).");

    return Path.Combine(dir.FullName, "data", "gateway_configs");
}
