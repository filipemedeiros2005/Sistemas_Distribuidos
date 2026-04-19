#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Npgsql;
using OneHealth.Common;

namespace OneHealth.Server
{
    class Program
    {
        private const int SERVER_PORT = 5005;
        private const string DB_CONNECTION = "Host=localhost;Username=postgres;Password=postgres;Database=onehealth";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== [SERVIDOR CENTRAL ONE HEALTH (POSTGRESQL)] ===");
            InitDatabase();
            
            // Inicia a tarefa paralela para receber o vídeo via UDP do Gateway
            _ = Task.Run(StartLiveStreamReceiverAsync);

            TcpListener listener = new TcpListener(IPAddress.Any, SERVER_PORT);
            listener.Start();
            Console.WriteLine($"[INFO] A ouvir Gateways na porta TCP {SERVER_PORT}...\n");

            while (true)
            {
                try {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    Console.WriteLine($"[REDE] Novo Gateway conectado: {client.Client.RemoteEndPoint}");
                    _ = Task.Run(() => HandleGatewayAsync(client));
                } catch { }
            }
        }

        private static void InitDatabase()
        {
            try {
                using var conn = new NpgsqlConnection(DB_CONNECTION);
                conn.Open();
                using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE IF NOT EXISTS telemetry (
                        id SERIAL PRIMARY KEY, sensor_id BIGINT NOT NULL, msg_type VARCHAR(20) NOT NULL,
                        data_type VARCHAR(20) NOT NULL, value REAL NOT NULL, timestamp TIMESTAMP NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS sensor_status (
                        sensor_id BIGINT PRIMARY KEY, status VARCHAR(20), last_seen TIMESTAMP
                    );
                    TRUNCATE TABLE telemetry RESTART IDENTITY CASCADE;
                    TRUNCATE TABLE sensor_status RESTART IDENTITY CASCADE;
                ", conn);
                cmd.ExecuteNonQuery();
                Console.WriteLine("[DB] Tabelas prontas e limpas para a defesa.");
            }
            catch (Exception ex) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ERRO DB] {ex.Message}"); Console.ResetColor(); }
        }

        private static async Task HandleGatewayAsync(TcpClient client)
        {
            using (client) using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[20]; 
                while (true) {
                    int bytesRead = await ReadExactlyAsync(stream, buffer, 20);
                    if (bytesRead == 0) break;

                    TelemetryPacket packet = TelemetryPacket.FromBytes(buffer);
                    if (packet.IsValid()) ProcessPacket(packet);
                }
            }
        }

        private static void ProcessPacket(TelemetryPacket packet)
        {
            DateTime time = DateTimeOffset.FromUnixTimeSeconds(packet.TimeStamp).LocalDateTime;
            
            if (packet.MsgType == MsgType.HELO || (packet.MsgType == MsgType.STATUS && packet.Value == 1)) {
                UpdateStatus(packet.SensorID, "ONLINE", time);
                Console.WriteLine($"[ESTADO] Sensor {packet.SensorID} ONLINE");
                return;
            }
            if (packet.MsgType == MsgType.BYE || (packet.MsgType == MsgType.STATUS && packet.Value == 0)) {
                UpdateStatus(packet.SensorID, "OFFLINE", time);
                Console.WriteLine($"[ESTADO] Sensor {packet.SensorID} OFFLINE");
                return;
            }

            if (packet.MsgType == MsgType.ALERT) Console.WriteLine($"[ALERTA] S{packet.SensorID} | {packet.DataType}: {packet.Value:F2}");
            else Console.WriteLine($"[DADOS] S{packet.SensorID} | {packet.DataType}: {packet.Value:F2}");

            SaveTelemetry(packet, time);
        }

        private static void UpdateStatus(uint sensorId, string status, DateTime time)
        {
            try {
                using var conn = new NpgsqlConnection(DB_CONNECTION); conn.Open();
                using var cmd = new NpgsqlCommand(@"
                    INSERT INTO sensor_status (sensor_id, status, last_seen) 
                    VALUES (@id, @st, @ts) 
                    ON CONFLICT (sensor_id) DO UPDATE SET status = EXCLUDED.status, last_seen = EXCLUDED.last_seen", conn);
                cmd.Parameters.AddWithValue("id", (long)sensorId);
                cmd.Parameters.AddWithValue("st", status);
                cmd.Parameters.AddWithValue("ts", time);
                cmd.ExecuteNonQuery();
            } catch(Exception e) { Console.WriteLine("[ERRO UPDATE DB] " + e.Message); }
        }

        private static void SaveTelemetry(TelemetryPacket packet, DateTime time)
        {
            try {
                using var conn = new NpgsqlConnection(DB_CONNECTION); conn.Open();
                using var cmd = new NpgsqlCommand("INSERT INTO telemetry (sensor_id, msg_type, data_type, value, timestamp) VALUES (@id, @m, @d, @v, @ts)", conn);
                cmd.Parameters.AddWithValue("id", (long)packet.SensorID);
                cmd.Parameters.AddWithValue("m", packet.MsgType.ToString());
                cmd.Parameters.AddWithValue("d", packet.DataType.ToString());
                cmd.Parameters.AddWithValue("v", packet.Value);
                cmd.Parameters.AddWithValue("ts", time);
                cmd.ExecuteNonQuery();
            } catch(Exception e) { Console.WriteLine("[ERRO SAVE DB] " + e.Message); }
        }

        private static async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count) {
            int total = 0; while (total < count) { int read = await stream.ReadAsync(buffer, total, count - total); if (read == 0) return 0; total += read; } return total;
        }

        private static async Task StartLiveStreamReceiverAsync() {
            int SERVER_UDP_PORT = 7000;
            string LIVE_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "server_live"));
            
            // Limpa a pasta no arranque para a defesa começar do zero
            if (Directory.Exists(LIVE_DIR)) {
                foreach (string file in Directory.GetFiles(LIVE_DIR)) File.Delete(file);
            } else {
                Directory.CreateDirectory(LIVE_DIR);
            }
            
            using var udpClient = new UdpClient(SERVER_UDP_PORT);
            Console.WriteLine($"[INFO] Servidor pronto a receber Live Streams UDP na porta {SERVER_UDP_PORT}...");
            
            while (true) {
                try {
                    var res = await udpClient.ReceiveAsync();
                    if (res.Buffer.Length >= 16) {
                        VideoPacketHeader header = VideoPacketHeader.FromBytes(res.Buffer);
                        byte[] p = new byte[res.Buffer.Length - 16]; 
                        Buffer.BlockCopy(res.Buffer, 16, p, 0, p.Length);
                        
                        using var fs = new FileStream(Path.Combine(LIVE_DIR, $"LIVE_S{header.SensorID}.raw"), FileMode.Append, FileAccess.Write, FileShare.None);
                        await fs.WriteAsync(p);
                        
                        if (header.SequenceNum % 10 == 0) {
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine($"[LIVE STREAM] Servidor recebeu Frame {header.SequenceNum} do Sensor {header.SensorID} via Borda!");
                            Console.ResetColor();
                        }
                    }
                } catch { }
            }
        }
    }
}