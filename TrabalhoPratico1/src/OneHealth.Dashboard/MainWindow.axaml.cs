#nullable enable
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Npgsql;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OneHealth.Dashboard;

public class AlertaItem {
    public string Texto { get; set; } = "";
}

public partial class MainWindow : Window
{
    private const string DB_CONNECTION = "Host=localhost;Username=postgres;Password=postgres;Database=onehealth";
    
    public ObservableCollection<AlertaItem> Alertas { get; set; } = new();
    public ObservableCollection<string> Sensores { get; set; } = new();
    public ObservableCollection<string> TelemetriaGlobal { get; set; } = new(); 
    
    private DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();
        LstAlerts.ItemsSource = Alertas; 
        LstSensors.ItemsSource = Sensores; 
        LstTelemetry.ItemsSource = TelemetriaGlobal; 
        
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => AtualizarDashboards();
        _timer.Start();
    }

    private void AtualizarDashboards()
    {
        try {
            using var conn = new NpgsqlConnection(DB_CONNECTION); conn.Open();
            CarregarSensores(conn); 
            CarregarDados(conn);
        } catch { }
    }

    private void CarregarSensores(NpgsqlConnection conn)
    {
        Sensores.Clear();
        using var cmd = new NpgsqlCommand("SELECT sensor_id, status, last_seen FROM sensor_status ORDER BY sensor_id", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            long id = reader.GetInt64(0);
            string status = reader.GetString(1);
            string statusIcon = status == "ONLINE" ? "[ONLINE]" : "[OFFLINE]";
            Sensores.Add($"{statusIcon} - Unidade Sensor {id}");
        }
    }

    private void CarregarDados(NpgsqlConnection conn) 
    {
        TelemetriaGlobal.Clear(); 
        Alertas.Clear();
        
        // AUMENTADO PARA 100 REGISTOS!
        using var cmdAll = new NpgsqlCommand("SELECT timestamp, sensor_id, data_type, value, msg_type FROM telemetry ORDER BY timestamp DESC LIMIT 100", conn);
        using var readerAll = cmdAll.ExecuteReader();
        while(readerAll.Read()) {
            string msg = readerAll.GetString(4);
            string linha = $"[{readerAll.GetDateTime(0):HH:mm:ss}] Sensor {readerAll.GetInt64(1)} -> {readerAll.GetString(2)}: {readerAll.GetFloat(3):F2}";
            
            if (msg == "ALERT") Alertas.Add(new AlertaItem { Texto = "[ALERTA] " + linha });
            else TelemetriaGlobal.Add(linha);
        }
        readerAll.Close();

        var txtGateway = this.FindControl<TextBlock>("TxtGatewayStats");
        var txtServer = this.FindControl<TextBlock>("TxtServerStats");

        string edgeDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "videos"));
        if (Directory.Exists(edgeDir) && txtGateway != null) {
            long edgeBytes = new DirectoryInfo(edgeDir).GetFiles("*.raw").Sum(f => f.Length);
            txtGateway.Text = $"Armazenamento Borda (Gateway): {edgeBytes / 1024} KB guardados localmente.";
        }

        string cloudDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "server_live"));
        if (Directory.Exists(cloudDir) && txtServer != null) {
            long cloudBytes = new DirectoryInfo(cloudDir).GetFiles("*.raw").Sum(f => f.Length);
            txtServer.Text = $"Backups de Emergencia (Servidor): {cloudBytes / 1024} KB recebidos.";
        }
    }

    public void BtnVerVideo_Click(object? sender, RoutedEventArgs e) {
        try {
            string cloudDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "server_live"));
            string possibleVideo = Path.Combine(cloudDir, "LIVE_S101.raw");
            string edgeDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "videos"));
            string possibleEdge = Path.Combine(edgeDir, "S101_Recording.raw");
            
            string videoPathToPlay = File.Exists(possibleVideo) ? possibleVideo : possibleEdge;

            if (File.Exists(videoPathToPlay)) {
                var player = new RawVideoPlayerWindow(videoPathToPlay);
                player.Show();
            } else {
                var msg = new Window { Title = "Erro", Width = 400, Height = 100, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                msg.Content = new TextBlock { Text = "Nenhum ficheiro RAW encontrado da anomalia deste sensor.", Margin = new Avalonia.Thickness(20), Foreground = Avalonia.Media.Brushes.White };
                msg.Show();
            }
        } catch { }
    }
}