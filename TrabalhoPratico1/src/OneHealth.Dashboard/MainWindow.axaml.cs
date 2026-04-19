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
        LstAlerts.ItemsSource = Alertas;
        LstSensors.ItemsSource = Sensores;
        LstTelemetry.ItemsSource = TelemetriaGlobal; 
        
        BtnStream.Click += BtnStream_Click;
        
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => AtualizarDashboards();
        _timer.Start();

        AtualizarDashboards();
    }

    private void AtualizarDashboards()
    {
        try
        {
            using var conn = new NpgsqlConnection(DB_CONNECTION);
            conn.Open();
            CarregarSensores(conn);
            CarregarAlertas(conn);
            CarregarDados(conn); 
        }
        catch (Exception ex)
        {
            Sensores.Clear();
            Sensores.Add($"[ERRO DB] Sem ligação ao PostgreSQL: {ex.Message}");
        }
    }

    private void CarregarDados(NpgsqlConnection conn) 
    {
        try 
        {
            // ABA 2: Telemetria Global (Filtra apenas dados úteis, ignora STATUS/HELO/BYE)
            TelemetriaGlobal.Clear();
            using var cmdAll = new NpgsqlCommand("SELECT timestamp, sensor_id, data_type, value FROM telemetry WHERE msg_type IN ('DATA', 'ALERT') ORDER BY id DESC LIMIT 15", conn);
            using var readerAll = cmdAll.ExecuteReader();
            while(readerAll.Read()) 
                TelemetriaGlobal.Add($"[{readerAll.GetDateTime(0):HH:mm:ss}] Sensor {readerAll.GetInt64(1)} -> {readerAll.GetString(2)}: {readerAll.GetFloat(3):F2}");
            readerAll.Close();

            // ABA 3: Alert Stats (Prova do tráfego de Vídeo na Borda)
            string videoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "videos", "S101_Recording.raw"));
            if (File.Exists(videoPath)) {
                var info = new FileInfo(videoPath);
                // Exibe o peso do ficheiro em KB para provar o transporte de bytes UDP
                if (TxtVideoStats != null)
                    TxtVideoStats.Text = $"📁 Backup de Vídeo na Borda (S101): {info.Length / 1024} KB recebidos via UDP.";
            }
        } 
        catch { }
    }

    private void CarregarSensores(NpgsqlConnection conn)
    {
        Sensores.Clear();
        using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT ON (sensor_id) sensor_id, data_type, value, timestamp, msg_type 
            FROM telemetry 
            ORDER BY sensor_id, timestamp DESC", conn);
            
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long id = reader.GetInt64(0);
            string tipo = reader.GetString(1);
            float valor = reader.GetFloat(2);
            DateTime tempo = reader.GetDateTime(3);
            string msgType = reader.GetString(4);

            bool isVivo = msgType != "BYE" && (DateTime.Now - tempo).TotalSeconds < 50;
            string statusIcon = isVivo ? "🟢 ATIVO " : "🔴 INATIVO";
            string detalhe = isVivo ? $"| Última Leitura: {valor:F2} ({tipo})" : "| Desconectado";

            Sensores.Add($"{statusIcon} - Unidade Sensor {id} {detalhe}");
        }
    }

    private void CarregarAlertas(NpgsqlConnection conn)
    {
        Alertas.Clear();
        using var cmd = new NpgsqlCommand(@"
            SELECT timestamp, sensor_id, data_type, value 
            FROM telemetry 
            WHERE msg_type = 'ALERT' 
            ORDER BY timestamp DESC LIMIT 20", conn);
            
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            DateTime tempo = reader.GetDateTime(0);
            long id = reader.GetInt64(1);
            string tipo = reader.GetString(2);
            float valor = reader.GetFloat(3);
            
            Alertas.Add($"[{tempo:HH:mm:ss}] ⚠️ EMERGÊNCIA NO SENSOR {id} | {tipo}: {valor:F2} (Vídeo de Borda Iniciado)");
        }
    }

    private void BtnStream_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string videoPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "S101_CCTV.mp4"));
            if (File.Exists(videoPath))
            {
                Process.Start(new ProcessStartInfo { FileName = videoPath, UseShellExecute = true });
            }
            else
            {
                Alertas.Insert(0, $"⚠️ Erro: Vídeo não encontrado em {videoPath}");
            }
        }
        catch { }
    }
}