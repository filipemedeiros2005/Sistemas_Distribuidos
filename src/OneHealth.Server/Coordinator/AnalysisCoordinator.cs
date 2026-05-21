using System.Net;
using System.Net.Sockets;
using System.Text;
using OneHealth.Server.Analysis;

namespace OneHealth.Server.Coordinator;

/// <summary>
/// Accepts TCP connections from the Dashboard on port 5006, parses each
/// pipe-delimited request, resolves zones to sensor ids, delegates to the
/// Python analysis service via gRPC, and responds with a pipe-delimited
/// result line.
///
/// Each connection runs in its own <see cref="Task"/> so a slow analysis
/// does not block other Dashboard instances. The listener stays alive until
/// the supplied <see cref="CancellationToken"/> is cancelled.
/// </summary>
public sealed class AnalysisCoordinator
{
    public const int DefaultPort = 5006;

    private readonly int _port;
    private readonly ZoneResolver _zoneResolver;
    private readonly AnalysisClient _analysisClient;

    public AnalysisCoordinator(int port, ZoneResolver zoneResolver, AnalysisClient analysisClient)
    {
        _port = port;
        _zoneResolver = zoneResolver;
        _analysisClient = analysisClient;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        Console.WriteLine($"[COORDINATOR] Listening on tcp://127.0.0.1:{_port}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
            Console.WriteLine("[COORDINATOR] Listener stopped.");
        }
    }

    // UTF-8 without BOM — TCP clients (Dashboard, nc, python) don't expect a BOM
    // and would parse it as garbage at the start of the response.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint;
        Console.WriteLine($"[COORDINATOR] Accepted {remote}");

        try
        {
            using (client)
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Utf8NoBom, leaveOpen: true);
                await using var writer = new StreamWriter(stream, Utf8NoBom) { AutoFlush = true, NewLine = "\n" };

                var raw = await reader.ReadLineAsync(cancellationToken);
                if (raw is null)
                {
                    Console.WriteLine($"[COORDINATOR] {remote}: empty request");
                    return;
                }

                var response = await DispatchAsync(raw, cancellationToken);
                await writer.WriteLineAsync(response);
                Console.WriteLine($"[COORDINATOR] {remote}: {raw}  ->  {Truncate(response, 80)}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[COORDINATOR] {remote}: handler failure: {ex.Message}");
        }
    }

    /// <summary>Routes a raw request to its handler and returns the response line.</summary>
    private async Task<string> DispatchAsync(string raw, CancellationToken cancellationToken)
    {
        if (AnalysisQueryParser.TryParseControl(raw, out var control))
            return control == "PING" ? "PONG|server=OneHealth.Server|status=ready" : $"ERROR|reason=unknown_control:{control}";

        AnalysisQuery query;
        try
        {
            query = AnalysisQueryParser.Parse(raw);
        }
        catch (FormatException ex)
        {
            return $"ERROR|reason=bad_request|detail={ex.Message}";
        }

        // Resolve sensor ids: explicit > zone > all (empty list).
        IReadOnlyList<uint> sensorIds = query.ExplicitSensorIds;
        if (sensorIds.Count == 0 && !string.IsNullOrEmpty(query.Zone))
        {
            sensorIds = await _zoneResolver.ResolveAsync(query.Zone, cancellationToken);
            if (sensorIds.Count == 0)
                return $"ERROR|reason=zone_empty|zone={query.Zone}";
        }

        var result = await _analysisClient.RunAnalysisAsync(query, sensorIds, cancellationToken);
        if (result is null)
            return "ERROR|reason=analysis_unavailable";

        // Day 5 will format full series; for now, summary + scalar metrics.
        var metrics = string.Join(",", result.Metrics.Select(kv => $"{kv.Key}={kv.Value:F4}"));
        return $"OK|kind={result.Kind}|summary={EscapePipes(result.SummaryText)}|metrics={metrics}";
    }

    private static string EscapePipes(string s) => s.Replace('|', '/');

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
