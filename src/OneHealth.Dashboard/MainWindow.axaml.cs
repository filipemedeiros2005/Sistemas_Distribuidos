using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using OneHealth.Dashboard.Data;
using SkiaSharp;

namespace OneHealth.Dashboard;

/// <summary>
/// Dashboard window. Wires the Analysis form to the Server's
/// AnalysisCoordinator over TCP, polls PostgreSQL on a 2-second tick to
/// keep the live Telemetry feed and the analysis-history ListBox fresh,
/// and renders FORECAST series in LiveCharts2.
/// </summary>
public partial class MainWindow : Window
{
    // ---- Server location --------------------------------------------------
    private const string ServerHost = "127.0.0.1";
    private const int    ServerPort = 5006;

    // ---- Video stream location (simulated CCTV feed) ----------------------
    private const string VideoHost = "127.0.0.1";
    private const int    VideoPort = 9000;
    private static readonly Encoding Utf8NoBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // ---- Bindings used by the XAML ---------------------------------------
    /// <summary>Series fed to the CartesianChart. Empty for non-FORECAST analyses.</summary>
    public ObservableCollection<ISeries> ChartSeries { get; } = new();

    /// <summary>Single X axis configured to format Unix-ms ticks as HH:mm:ss.</summary>
    public Axis[] ChartXAxes { get; } =
    {
        new()
        {
            Labeler = value => value > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)value)
                                .ToLocalTime().ToString("HH:mm:ss")
                : string.Empty,
            LabelsRotation = 15,
        }
    };

    /// <summary>Most recent analyses (refreshed by timer).</summary>
    public ObservableCollection<AnalysisListItem> HistoryItems { get; } = new();

    /// <summary>Most recent telemetry rows (refreshed by timer).</summary>
    public ObservableCollection<TelemetryRow> TelemetryRows { get; } = new();

    /// <summary>Sensor registry rows (refreshed by timer).</summary>
    public ObservableCollection<SensorRow> SensorRows { get; } = new();

    /// <summary>Options for the Analysis "Sensor" filter: "(all)" plus each known
    /// sensor id. Grown in place (never cleared) so the user's current selection
    /// is preserved across the 2-second refresh.</summary>
    public ObservableCollection<string> SensorFilterOptions { get; } = new() { "(all)" };

    // ---- Wiring -----------------------------------------------------------
    private readonly AnalysisRepository _analysisRepo;
    private readonly TelemetryRepository _telemetryRepo;
    private readonly SensorRepository _sensorRepo;
    private readonly DispatcherTimer _timer;

    /// <summary>Marks an id that was just submitted so the next refresh auto-selects it.</summary>
    private long? _pendingSelectId;

    /// <summary>Tracks the analysis currently rendered in the chart, so the 2-second
    /// history refresh — which triggers a spurious SelectionChanged when it rebuilds
    /// the ListBox — does not re-draw the chart with rotated palette colors.</summary>
    private long? _renderedAnalysisId;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        var dsn = PgConnectionString();
        _analysisRepo  = new AnalysisRepository(dsn);
        _telemetryRepo = new TelemetryRepository(dsn);
        _sensorRepo    = new SensorRepository(dsn);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAllAsync();
        _timer.Start();

        // First refresh up-front so the UI is populated before the first tick.
        Dispatcher.UIThread.Post(async () => await RefreshAllAsync(),
                                 DispatcherPriority.Background);
    }

    // =====================================================================
    // Analysis form actions
    // =====================================================================

    private async void OnPingClicked(object? sender, RoutedEventArgs e)
    {
        await DispatchAsync("PING");
    }

    private async void OnRunAnalysisClicked(object? sender, RoutedEventArgs e)
    {
        var zone   = (CmbZone.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "(all)";
        var kind   = (CmbKind.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "AVG";
        var window = (TxtWindow.Text ?? "60").Trim();
        var types  = (TxtTypes.Text  ?? "").Trim();
        var sensor = CmbSensor.SelectedItem as string ?? "(all)";

        var parts = new List<string> { $"KIND={kind}", $"WINDOW={window}" };
        // A specific sensor takes precedence over the zone (the server resolves
        // SENSORS first and only falls back to ZONA when no sensor is given).
        if (sensor != "(all)")  parts.Add($"SENSORS={sensor}");
        else if (zone != "(all)") parts.Add($"ZONA={zone}");
        if (types.Length > 0)   parts.Add($"TYPES={types}");
        if (kind == "FORECAST") parts.Add("HORIZON=10");

        await DispatchAsync(string.Join('|', parts));
    }

    /// <summary>
    /// Round-trip TCP call to the AnalysisCoordinator. On a successful
    /// <c>OK|id=N|...</c> response we trigger an immediate history refresh
    /// and auto-select the new row, so the chart updates without an extra
    /// click. UI mutation always goes through the dispatcher thread.
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
                AutoFlush = true, NewLine = "\n"
            };
            using var reader = new StreamReader(stream, Utf8NoBom);

            await writer.WriteLineAsync(request);
            var response = await reader.ReadLineAsync() ?? "(empty response)";

            // If we got an OK with an id, auto-select that row after refresh.
            var fields = ParseFields(response);
            var verb = fields.Count > 0 ? fields[0].Key : "?";
            if (verb == "OK")
            {
                var idField = fields.FirstOrDefault(f =>
                    string.Equals(f.Key, "id", StringComparison.OrdinalIgnoreCase));
                if (long.TryParse(idField.Value, out var newId))
                    _pendingSelectId = newId;
                await RefreshAllAsync();
            }

            SetStatus(verb switch
            {
                "OK"    => $"Done — see history below.",
                "ERROR" => $"Server returned an error: {response}",
                "PONG"  => response,
                _       => response,
            });
        }
        catch (SocketException ex)
        {
            SetStatus($"Could not reach server at {ServerHost}:{ServerPort} — {ex.Message}");
        }
        catch (Exception ex)
        {
            SetStatus($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
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
    /// Opens a live-feed window for the sensor whose "Live" button was clicked.
    /// The sensor id travels on the button's Tag (set in the DataGrid template).
    /// </summary>
    private void OnLiveClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int sensorId }) return;

        var player = new VideoPlayerWindow((uint)sensorId, VideoHost, VideoPort);
        player.Show(this);
    }

    // =====================================================================
    // Refresh tick (history + telemetry)
    // =====================================================================

    private async Task RefreshAllAsync()
    {
        try { await RefreshHistoryAsync(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DASHBOARD] history refresh failed: {ex.Message}");
        }
        try { await RefreshTelemetryAsync(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DASHBOARD] telemetry refresh failed: {ex.Message}");
        }
        try { await RefreshSensorsAsync(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DASHBOARD] sensors refresh failed: {ex.Message}");
        }
    }

    private async Task RefreshHistoryAsync()
    {
        var rows = await _analysisRepo.ListRecentAsync(20);

        // Flicker fix: remember selection and restore it after rebuild.
        var previouslySelectedId = (LstHistory.SelectedItem as AnalysisListItem)?.Id;
        var targetId = _pendingSelectId ?? previouslySelectedId;
        _pendingSelectId = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            HistoryItems.Clear();
            foreach (var row in rows) HistoryItems.Add(row);

            if (targetId is long id)
            {
                var match = HistoryItems.FirstOrDefault(x => x.Id == id);
                if (match != null) LstHistory.SelectedItem = match;
            }
        });
    }

    private async Task RefreshTelemetryAsync()
    {
        var rows = await _telemetryRepo.ListRecentAsync(50);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TelemetryRows.Clear();
            foreach (var r in rows) TelemetryRows.Add(r);
        });
    }

    private async Task RefreshSensorsAsync()
    {
        var rows = await _sensorRepo.ListAllAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SensorRows.Clear();
            foreach (var r in rows) SensorRows.Add(r);

            // Grow the Analysis sensor-filter options in place: append any id we
            // haven't seen yet, keeping "(all)" first and the user's selection intact.
            foreach (var r in rows)
            {
                var id = r.SensorId.ToString();
                if (!SensorFilterOptions.Contains(id))
                    SensorFilterOptions.Add(id);
            }
        });
    }

    // =====================================================================
    // Lazy-load chart on selection
    // =====================================================================

    private async void OnHistorySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (LstHistory.SelectedItem is not AnalysisListItem item) return;

        // Skip the round-trip if the selected analysis is already on screen.
        // RefreshHistoryAsync rebuilds the ListBox every 2 s; without this guard
        // the chart would be re-drawn (and series colors rotated) on every tick.
        if (_renderedAnalysisId == item.Id) return;

        AnalysisDetail? detail;
        try
        {
            detail = await _analysisRepo.GetByIdAsync(item.Id);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                TxtDetails.Text = $"Failed to load analysis #{item.Id}: {ex.Message}");
            return;
        }

        if (detail is null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                TxtDetails.Text = $"Analysis #{item.Id} not found.");
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RenderChart(detail);
            TxtDetails.Text = FormatDetails(detail);
        });
    }

    /// <summary>
    /// Renders one <see cref="LineSeries{TModel}"/> per label in the result.
    /// Point series ("values", "historical", "normal", "anomaly", "forecast")
    /// are drawn with markers; reference lines ("average", "mean", "±1 sigma")
    /// are thin, marker-less, straight lines. Colors are pinned per label so
    /// successive renders stay deterministic (no palette-rotation flicker).
    /// </summary>
    private void RenderChart(AnalysisDetail detail)
    {
        ChartSeries.Clear();
        _renderedAnalysisId = detail.Id;
        if (detail.Series.Count == 0) return;

        var grouped = detail.Series
            .GroupBy(p => string.IsNullOrEmpty(p.Label) ? "values" : p.Label)
            .OrderBy(g => SeriesOrder(g.Key));

        foreach (var group in grouped)
        {
            var color = ColorFor(group.Key);
            var isReferenceLine = IsReferenceLine(group.Key);

            ChartSeries.Add(new LineSeries<ObservablePoint>
            {
                Name           = group.Key,
                Values         = group.Select(p => new ObservablePoint(p.Ts, p.Value)).ToArray(),
                GeometrySize   = isReferenceLine ? 0 : 6,
                LineSmoothness = 0,
                Stroke         = new SolidColorPaint(color, isReferenceLine ? 1.5f : 2f),
                GeometryStroke = new SolidColorPaint(color, 2),
                GeometryFill   = new SolidColorPaint(color),
                Fill           = null,
            });
        }
    }

    /// <summary>Reference lines render behind the measurement points.</summary>
    private static int SeriesOrder(string label) => label switch
    {
        "values" or "historical" or "normal" => 0,
        "anomaly" or "forecast"              => 1,
        _                                    => 2,  // reference lines on top
    };

    private static bool IsReferenceLine(string label) =>
        label is "average" or "mean" or "+1 sigma" or "-1 sigma";

    /// <summary>Deterministic color per series label so re-renders never flicker.</summary>
    private static SKColor ColorFor(string label) => label switch
    {
        "historical" or "values" or "normal" => new SKColor(0xFF, 0xB3, 0x00), // amber
        "forecast"                           => new SKColor(0x00, 0xB0, 0xFF), // deep sky blue
        "anomaly"                            => new SKColor(0xFF, 0x3D, 0x00), // red-orange
        "average" or "mean"                  => new SKColor(0x00, 0xE5, 0xFF), // cyan line
        "+1 sigma" or "-1 sigma"             => new SKColor(0x76, 0xFF, 0x03), // green band
        _                                    => new SKColor(0x9E, 0x9E, 0x9E), // grey fallback
    };

    private static string FormatDetails(AnalysisDetail d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"#{d.Id}  {d.Kind}  ({d.ProducedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss})");
        sb.AppendLine($"summary: {d.Summary}");
        if (d.Metrics.Count > 0)
        {
            sb.AppendLine("metrics:");
            foreach (var (k, v) in d.Metrics.OrderBy(kv => kv.Key))
                sb.AppendLine($"  {k} = {v:G6}");
        }
        if (d.Series.Count > 0)
            sb.AppendLine($"series: {d.Series.Count} point(s) — see chart");
        return sb.ToString().TrimEnd();
    }

    // =====================================================================
    // Plumbing
    // =====================================================================

    private static List<KeyValuePair<string, string>> ParseFields(string line)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var raw in line.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = raw.IndexOf('=');
            result.Add(idx < 0
                ? new KeyValuePair<string, string>(raw.Trim(), string.Empty)
                : new KeyValuePair<string, string>(raw[..idx].Trim(), raw[(idx + 1)..].Trim()));
        }
        return result;
    }

    private void SetStatus(string text) =>
        Dispatcher.UIThread.Post(() => LblStatus.Text = text);

    private static string PgConnectionString() =>
        Environment.GetEnvironmentVariable("ONEHEALTH_PG_CONN_DASHBOARD")
        ?? $"Host=localhost;Port=5432;Database=onehealth;Username={Environment.UserName}";
}
