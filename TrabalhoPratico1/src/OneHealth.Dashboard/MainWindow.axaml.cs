#nullable enable
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Npgsql;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace OneHealth.Dashboard;

public partial class MainWindow : Window
{
    private const string DB_CONNECTION = "Host=localhost;Username=postgres;Password=postgres;Database=onehealth";
    
    public ObservableCollection<string> Alertas { get; set; } = new();
    public ObservableCollection<string> Sensores { get; set; } = new();
    public ObservableCollection<string> TelemetriaGlobal { get; set; } = new(); 
    
    private DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();
        LstAlerts.ItemsSource = Alertas; LstSensors.ItemsSource = Sensores; LstTelemetry.ItemsSource = TelemetriaGlobal; 
        BtnStream.Click += BtnStream_Click;
        
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => AtualizarDashboards();
        _timer.Start();
    }

    private void AtualizarDashboards()
    {
        try {
            using var conn = new NpgsqlConnection(DB_CONNECTION); conn.Open();
            CarregarSensores(conn); CarregarDados(conn);
        } catch { }
    }

    private void CarregarSensores(NpgsqlConnection conn)
    {
        Sensores.Clear();
        // LÊ DIRETAMENTE DA NOVA TABELA DE ESTADOS! Sem matemáticas de fusos horários.
        using var cmd = new NpgsqlCommand("SELECT sensor_id, status, last_seen FROM sensor_status ORDER BY sensor_id", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            long id = reader.GetInt64(0);
            string status = reader.GetString(1);
            string statusIcon = status == "ONLINE" ? "🟢 ONLINE" : "🔴 OFFLINE";
            Sensores.Add($"{statusIcon} - Unidade Sensor {id}");
        }
    }

    private void CarregarDados(NpgsqlConnection conn) 
    {
        TelemetriaGlobal.Clear(); Alertas.Clear();
        
        using var cmdAll = new NpgsqlCommand("SELECT timestamp, sensor_id, data_type, value, msg_type FROM telemetry ORDER BY id DESC LIMIT 15", conn);
        using var readerAll = cmdAll.ExecuteReader();
        while(readerAll.Read()) {
            string msg = readerAll.GetString(4);
            string linha = $"[{readerAll.GetDateTime(0):HH:mm:ss}] Sensor {readerAll.GetInt64(1)} -> {readerAll.GetString(2)}: {readerAll.GetFloat(3):F2}";
            
            if (msg == "ALERT") Alertas.Add("⚠️ " + linha);
            else TelemetriaGlobal.Add(linha);
        }
        readerAll.Close();

        string videoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "videos", "S101_Recording.raw"));
        if (File.Exists(videoPath)) TxtVideoStats.Text = $"📁 Tamanho do Vídeo na Borda: {new FileInfo(videoPath).Length / 1024} KB (Transporte UDP OK)";
    }

    private void BtnStream_Click(object? sender, RoutedEventArgs e) {
        try {
            string videoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "S101_CCTV.mp4"));
            if (File.Exists(videoPath)) Process.Start(new ProcessStartInfo { FileName = videoPath, UseShellExecute = true });
        } catch { }
    }
}