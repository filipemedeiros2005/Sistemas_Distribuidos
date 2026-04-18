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
    
    private DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();
        LstAlerts.ItemsSource = Alertas;
        LstSensors.ItemsSource = Sensores;
        
        // Ligação do botão feita no C# para evitar erros do compilador XAML
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
        }
        catch (Exception ex)
        {
            Sensores.Clear();
            Sensores.Add($"[ERRO DB] Sem ligação ao PostgreSQL: {ex.Message}");
        }
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = videoPath,
                    UseShellExecute = true
                });
            }
            else
            {
                Sensores.Insert(0, $"⚠️ Erro: Vídeo não encontrado em {videoPath}");
            }
        }
        catch { }
    }
}