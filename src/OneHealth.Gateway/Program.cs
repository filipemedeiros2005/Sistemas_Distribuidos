using System.Globalization;
using Grpc.Core;
using OneHealth.Common;
using OneHealth.Gateway.Configuration;
using OneHealth.Gateway.Consuming;
using OneHealth.Gateway.Preprocessing;
using OneHealth.Gateway.Publishing;
using OneHealth.Gateway.Registry;

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

    await using var registry = new SensorRegistry(PgConnectionString());
    await registry.InitSchemaAsync();
    Console.WriteLine("[BOOT] PostgreSQL connected; 'sensors' table ready.");

    await using var aggregator = new AggregatePublisher(options.Port);
    await aggregator.ConnectAsync();
    Console.WriteLine("[BOOT] Aggregator publisher → exchange 'onehealth.aggregated'");

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

        // Hello / Status / Bye → refresh the sensor's row in the registry so
        // the Preprocessor's authorisation check (and any future watchdog)
        // can rely on a live view of who is alive.
        if (packet.MsgType is MsgType.Hello or MsgType.Status or MsgType.Bye)
        {
            if (options.AllowedSensors.TryGetValue(packet.SensorId, out var entry))
            {
                var newStatus = packet.MsgType == MsgType.Bye ? "OFFLINE" : "ONLINE";
                try { await registry.UpsertAsync(packet.SensorId, entry.Zone, newStatus, cts.Token); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[REGISTRY] UPSERT failed for sid={packet.SensorId}: {ex.Message}");
                }
            }
            else
            {
                Console.Error.WriteLine($"[REGISTRY] {packet.MsgType} from unknown sid={packet.SensorId} — skipping UPSERT");
            }
        }

        // Design rule 2.5: only Data packets go through pre-processing.
        // Hello/Bye/Status/Alert bypass the RPC entirely.
        if (packet.MsgType != MsgType.Data)
        {
            bypassed++;
            Console.WriteLine(
                $"[BYPASS #{recv:D4}] {packet.MsgType,-6} {packet.DataType,-11} sid={packet.SensorId}");

            // Alerts carry real anomaly readings — forward them downstream so
            // the Server can persist + show them on the Dashboard. Hello/Status/Bye
            // are control messages with no measurement value: kept in the registry
            // only, never aggregated.
            if (packet.MsgType == MsgType.Alert &&
                options.AllowedSensors.TryGetValue(packet.SensorId, out var alertEntry))
            {
                await aggregator.PublishAsync(packet, alertEntry.Zone, cts.Token);
            }
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

            // Substitute the value with the canonical/normalised one and
            // re-publish to the aggregated exchange for the Server.
            var normalised = packet with { Value = (float)result.Value };
            if (options.AllowedSensors.TryGetValue(packet.SensorId, out var entry))
            {
                await aggregator.PublishAsync(normalised, entry.Zone, cts.Token);
            }

            fwd++;
            Console.WriteLine(
                $"[FWD    #{recv:D4}] {packet.DataType,-11} sid={packet.SensorId,-3} " +
                $"raw={packet.Value,9:F2} norm={result.Value,9:F2}");
            return ConsumeOutcome.Ack;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            // Truly transient — service down or slow. Requeue so the message
            // comes back when the service is healthy again.
            retried++;
            Console.Error.WriteLine(
                $"[RETRY  #{recv:D4}] {packet.DataType} sid={packet.SensorId} — transient ({ex.StatusCode}): {ex.Status.Detail}");
            return ConsumeOutcome.RequeueAndRetry;
        }
        catch (RpcException ex)
        {
            // Server-side bug (Unknown/Internal/etc.) — requeuing would loop
            // forever. Drop as poison so the system stays healthy; log loudly
            // because this is something the operator must investigate.
            dropped++;
            Console.Error.WriteLine(
                $"[POISON #{recv:D4}] {packet.DataType} sid={packet.SensorId} — RPC fault ({ex.StatusCode}): {ex.Status.Detail}");
            return ConsumeOutcome.DropPoison;
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

static string PgConnectionString() =>
    Environment.GetEnvironmentVariable("ONEHEALTH_PG_CONN")
    ?? $"Host=localhost;Port=5432;Database=onehealth;Username={Environment.UserName}";

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
