using System.Globalization;
using Grpc.Core;
using OneHealth.Common;
using OneHealth.Gateway.Configuration;
using OneHealth.Gateway.Consuming;
using OneHealth.Gateway.Preprocessing;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

try
{
    var configDir = ResolveGatewayConfigDir();
    var options = GatewayOptions.Parse(args, configDir);

    Console.WriteLine(
        $"[BOOT] Gateway port={options.Port} | zones=[{string.Join(", ", options.Zones)}] | " +
        $"sensors={options.AllowedSensors.Count}");

    using var preprocessor = new PreprocessorClient();
    Console.WriteLine("[BOOT] Pre-processor client → http://localhost:50051");

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

    var recv = 0; var fwd = 0; var dropped = 0; var bypassed = 0; var retried = 0;

    await consumer.ConsumeAsync(async (packet, routingKey) =>
    {
        recv++;

        // Design rule 2.5: only Data packets go through pre-processing.
        // Hello/Bye/Status/Alert bypass the RPC entirely.
        if (packet.MsgType != MsgType.Data)
        {
            bypassed++;
            Console.WriteLine(
                $"[BYPASS #{recv:D4}] {packet.MsgType,-6} {packet.DataType,-11} sid={packet.SensorId}");
            return ConsumeOutcome.Ack;
        }

        try
        {
            var result = await preprocessor.NormalizeAsync(packet, cts.Token);

            if (result.Dropped)
            {
                dropped++;
                Console.WriteLine(
                    $"[DROP   #{recv:D4}] {packet.DataType,-11} sid={packet.SensorId,-3} " +
                    $"raw={packet.Value,9:F2} reason={result.DropReason}");
                return ConsumeOutcome.Ack;     // processed cleanly — message done
            }

            fwd++;
            Console.WriteLine(
                $"[FWD    #{recv:D4}] {packet.DataType,-11} sid={packet.SensorId,-3} " +
                $"raw={packet.Value,9:F2} norm={result.Value,9:F2}");
            return ConsumeOutcome.Ack;
        }
        catch (RpcException ex)
        {
            // Pre-processor down or unreachable. Requeue so the message comes
            // back when the service is healthy again. The broker has it stored.
            retried++;
            Console.Error.WriteLine(
                $"[RETRY  #{recv:D4}] {packet.DataType} sid={packet.SensorId} — pre-processor unreachable " +
                $"({ex.StatusCode}): {ex.Status.Detail}");
            return ConsumeOutcome.RequeueAndRetry;
        }
    }, cts.Token);

    Console.WriteLine(
        $"[SHUTDOWN] recv={recv} fwd={fwd} dropped={dropped} bypassed={bypassed} retried={retried}. Goodbye.");
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
