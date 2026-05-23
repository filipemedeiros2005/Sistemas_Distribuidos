using System.Globalization;
using OneHealth.Common;
using OneHealth.Server.Aggregation;
using OneHealth.Server.Analysis;
using OneHealth.Server.Coordinator;
using OneHealth.Server.Persistence;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

try
{
    Console.WriteLine("[BOOT] Server starting...");

    await using var writer = new TelemetryWriter(PgConnectionString());
    await writer.InitSchemaAsync();
    Console.WriteLine("[BOOT] PostgreSQL connected; 'telemetry' table ready.");

    await using var consumer = new AggregateConsumer();
    await consumer.ConnectAsync();
    Console.WriteLine($"[BOOT] Subscribed to '{AggregateConsumer.ExchangeName}' via queue '{AggregateConsumer.QueueName}'.");

    await using var zoneResolver = new ZoneResolver(PgConnectionString());
    using var analysisClient = new AnalysisClient();
    await using var resultWriter = new AnalysisResultWriter(PgConnectionString());
    await resultWriter.InitSchemaAsync();
    Console.WriteLine("[BOOT] 'analysis_results' table ready.");

    var coordinator = new AnalysisCoordinator(
        AnalysisCoordinator.DefaultPort, zoneResolver, analysisClient, resultWriter);
    Console.WriteLine("[BOOT] AnalysisCoordinator wired (Python client at http://localhost:50052).");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.WriteLine("\n[SHUTDOWN] Ctrl+C received, stopping...");
    };
    using var sigterm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
        System.Runtime.InteropServices.PosixSignal.SIGTERM,
        ctx => { ctx.Cancel = true; cts.Cancel(); Console.WriteLine("\n[SHUTDOWN] SIGTERM received, stopping..."); });

    var count = 0;
    var consumerTask = consumer.ConsumeAsync(async (packet, routingKey) =>
    {
        count++;
        await writer.InsertAsync(packet, cts.Token);
        Console.WriteLine(
            $"[PERSIST #{count:D5}] {packet.MsgType,-6} {packet.DataType,-11} sid={packet.SensorId,-3} val={packet.Value,9:F2}");
    }, cts.Token);

    var coordinatorTask = coordinator.RunAsync(cts.Token);

    // Whichever finishes first signals shutdown — usually both react to the
    // cancellation token together when SIGTERM/Ctrl+C lands.
    await Task.WhenAny(consumerTask, coordinatorTask);
    cts.Cancel();
    await Task.WhenAll(
        consumerTask.ContinueWith(_ => { }),
        coordinatorTask.ContinueWith(_ => { }));

    Console.WriteLine($"[SHUTDOWN] Persisted {count} measurements. Goodbye.");
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
