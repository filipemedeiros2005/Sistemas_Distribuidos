using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace OneHealth.Dashboard;

/// <summary>
/// Analysis tab driver. Builds a pipe-delimited request from the form
/// controls, sends it over TCP to the Server's AnalysisCoordinator
/// (127.0.0.1:5006), parses the pipe-delimited response, and renders it in
/// a human-friendly multi-line layout in the response TextBox.
///
/// The live telemetry tab is still a placeholder — populated on Day 6.
/// </summary>
public partial class MainWindow : Window
{
    private const string ServerHost = "127.0.0.1";
    private const int    ServerPort = 5006;
    private static readonly Encoding Utf8NoBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnPingClicked(object? sender, RoutedEventArgs e)
    {
        await DispatchAsync("PING");
    }

    private async void OnRunAnalysisClicked(object? sender, RoutedEventArgs e)
    {
        var zone = (CmbZone.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "(all)";
        var kind = (CmbKind.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "AVG";
        var window = (TxtWindow.Text ?? "60").Trim();
        var types  = (TxtTypes.Text  ?? "").Trim();

        var parts = new List<string> { $"KIND={kind}", $"WINDOW={window}" };
        if (zone != "(all)") parts.Add($"ZONA={zone}");
        if (types.Length > 0) parts.Add($"TYPES={types}");
        if (kind == "FORECAST") parts.Add("HORIZON=10");

        var request = string.Join('|', parts);
        await DispatchAsync(request);
    }

    /// <summary>
    /// Single round-trip TCP call to the AnalysisCoordinator. All UI mutation
    /// happens on the dispatcher thread.
    /// </summary>
    private async Task DispatchAsync(string request)
    {
        SetStatus($"Sending: {request} ...");
        BtnPing.IsEnabled = false;
        BtnRunAnalysis.IsEnabled = false;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ServerHost, ServerPort);

            await using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Utf8NoBom)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            using var reader = new StreamReader(stream, Utf8NoBom);

            await writer.WriteLineAsync(request);
            var response = await reader.ReadLineAsync() ?? "(empty response)";

            var pretty = PrettyPrintResponse(request, response);

            await Dispatcher.UIThread.InvokeAsync(() => TxtResponse.Text = pretty);
            SetStatus($"Done ({response.Length} bytes).");
        }
        catch (SocketException ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                TxtResponse.Text =
                    $"Could not reach server at {ServerHost}:{ServerPort}\n{ex.Message}");
            SetStatus("Connection refused — is the Server up?");
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                TxtResponse.Text = $"Unexpected error: {ex.GetType().Name}: {ex.Message}");
            SetStatus("Error — see response box.");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BtnPing.IsEnabled = true;
                BtnRunAnalysis.IsEnabled = true;
            });
        }
    }

    /// <summary>
    /// Turns the wire payload (e.g.
    /// <c>OK|kind=AVG|summary=...|metrics=avg=27.25,count=12.0</c>) into a
    /// multi-line, vertically aligned display. Falls back to the raw payload
    /// for shapes we don't recognise (forward-compatible).
    /// </summary>
    private static string PrettyPrintResponse(string request, string response)
    {
        var fields = ParseFields(response);
        if (fields.Count == 0)
            return $">>> {request}\n<<< {response}";

        var verb = fields[0].Key; // first segment is the verb: OK / ERROR / PONG
        var rest = fields.Skip(1).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.Append(">>> ").AppendLine(request);
        sb.Append("<<< ").AppendLine(verb);

        if (rest.TryGetValue("kind", out var kind))
            sb.Append("    kind:    ").AppendLine(kind);

        if (rest.TryGetValue("summary", out var summary))
            sb.Append("    summary: ").AppendLine(summary);

        if (rest.TryGetValue("reason", out var reason))
            sb.Append("    reason:  ").AppendLine(reason);

        if (rest.TryGetValue("detail", out var detail))
            sb.Append("    detail:  ").AppendLine(detail);

        if (rest.TryGetValue("metrics", out var metrics) && metrics.Length > 0)
        {
            sb.AppendLine("    metrics:");
            foreach (var (k, v) in ParseCsvKv(metrics))
                sb.Append("      ").Append(k).Append(" = ").AppendLine(v);
        }

        // Surface anything we didn't explicitly handle so the user can still see it.
        var handled = new HashSet<string>(
            new[] { "kind", "summary", "reason", "detail", "metrics" },
            StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in rest)
            if (!handled.Contains(k))
                sb.Append("    ").Append(k).Append(": ").AppendLine(v);

        return sb.ToString().TrimEnd();
    }

    private static List<KeyValuePair<string, string>> ParseFields(string line)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var raw in line.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = raw.IndexOf('=');
            if (idx < 0)
                result.Add(new KeyValuePair<string, string>(raw.Trim(), string.Empty));
            else
                result.Add(new KeyValuePair<string, string>(raw[..idx].Trim(), raw[(idx + 1)..].Trim()));
        }
        return result;
    }

    private static IEnumerable<(string Key, string Value)> ParseCsvKv(string csv)
    {
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = token.IndexOf('=');
            if (idx <= 0) continue;
            yield return (token[..idx].Trim(), token[(idx + 1)..].Trim());
        }
    }

    private void SetStatus(string text) =>
        Dispatcher.UIThread.Post(() => LblStatus.Text = text);
}
