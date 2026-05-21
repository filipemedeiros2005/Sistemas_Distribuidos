using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace OneHealth.Dashboard;

/// <summary>
/// Day 4 mini-spike: validates the Dashboard ↔ Server pipe-delimited
/// protocol without touching the Python analysis service. The Analysis tab
/// builds a request, sends it over TCP to 127.0.0.1:5006, and dumps the
/// raw response into the read-only TextBox.
///
/// On Day 5/6 the response will be parsed into a typed model and rendered
/// with LiveCharts; for now it stays as the inspectable wire payload.
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

        var parts = new List<string> { $"KIND={kind}", "WINDOW=60" };
        if (zone != "(all)") parts.Add($"ZONA={zone}");
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

            await Dispatcher.UIThread.InvokeAsync(() =>
                TxtResponse.Text = $">>> {request}\n<<< {response}");
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

    private void SetStatus(string text) =>
        Dispatcher.UIThread.Post(() => LblStatus.Text = text);
}
