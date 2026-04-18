#nullable enable
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Npgsql; // Biblioteca do Postgres
using OneHealth.Common;

namespace OneHealth.Server
{
    class Program
    {
        private const int SERVER_PORT = 5005;
        // String de ligação ao PostgreSQL (Ajusta a password se necessário)
        private const string DB_CONNECTION = "Host=localhost;Username=postgres;Password=postgres;Database=onehealth";

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== [SERVIDOR CENTRAL ONE HEALTH (POSTGRESQL)] ===");

            // Inicializar Base de Dados
            InitDatabase();
            
            TcpListener listener = new TcpListener(IPAddress.Any, SERVER_PORT);
            listener.Start();
            Console.WriteLine($"[INFO] A ouvir Gateways na porta TCP {SERVER_PORT}...\n");

            while (true)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    string gatewayEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "IP Desconhecido";
                    Console.WriteLine($"[REDE] Novo Gateway conectado: {gatewayEndpoint}");
                    _ = Task.Run(() => HandleGatewayAsync(client, gatewayEndpoint));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO REDE] Falha ao aceitar conexão: {ex.Message}");
                }
            }
        }

        private static void InitDatabase()
        {
            try
            {
                using var conn = new NpgsqlConnection(DB_CONNECTION);
                conn.Open();
                
                // Criação da Tabela (se não existir)
                using var cmd = new NpgsqlCommand(@"
                    CREATE TABLE IF NOT EXISTS telemetry (
                        id SERIAL PRIMARY KEY,
                        sensor_id BIGINT NOT NULL,
                        msg_type VARCHAR(20) NOT NULL,
                        data_type VARCHAR(20) NOT NULL,
                        value REAL NOT NULL,
                        timestamp TIMESTAMP NOT NULL
                    )", conn);
                cmd.ExecuteNonQuery();
                Console.WriteLine("[DB] PostgreSQL conectado e tabela verificada.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[DB ERRO FATAL] Não foi possível ligar ao Postgres. Confirma se o serviço está a correr. Erro: {ex.Message}");
                Console.ResetColor();
                // Opcional: Environment.Exit(1); // Se quiseres que o servidor não arranque sem BD
            }
        }

        private static async Task HandleGatewayAsync(TcpClient client, string endpoint)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[20]; 
                while (true)
                {
                    try 
                    {
                        int bytesRead = await ReadExactlyAsync(stream, buffer, 20);
                        if (bytesRead == 0) break;

                        TelemetryPacket packet = TelemetryPacket.FromBytes(buffer);
                        if (!packet.IsValid()) continue; 

                        ProcessPacket(packet);
                    }
                    catch { break; }
                }
            }
        }

        private static void ProcessPacket(TelemetryPacket packet)
        {
            DateTime time = DateTimeOffset.FromUnixTimeSeconds(packet.TimeStamp).LocalDateTime;
            
            if (packet.MsgType == MsgType.STATUS) {
                Console.WriteLine($"ℹ️  [STATUS UPDATE] Sensor {packet.SensorID} reportado como INATIVO/ATIVO.");
                return;
            }
            
            if (packet.MsgType == MsgType.BYE) {
                Console.WriteLine($"[DESCONEXÃO] Sensor {packet.SensorID} saiu.");
                return;
            }

            if (packet.MsgType == MsgType.ALERT)
                Console.WriteLine($"⚠️ [ALERTA] SensorID: {packet.SensorID} | Valor: {packet.Value:F2}");
            else
                Console.WriteLine($"[TELEMETRIA] SensorID: {packet.SensorID} | Valor: {packet.Value:F2}");

            // Gravar na Base de Dados Postgres
            SaveToDatabase(packet, time);
        }

        private static void SaveToDatabase(TelemetryPacket packet, DateTime time)
        {
            try
            {
                using var conn = new NpgsqlConnection(DB_CONNECTION);
                conn.Open();
                using var cmd = new NpgsqlCommand(@"
                    INSERT INTO telemetry (sensor_id, msg_type, data_type, value, timestamp) 
                    VALUES (@s_id, @m_type, @d_type, @val, @ts)", conn);
                
                cmd.Parameters.AddWithValue("s_id", (long)packet.SensorID);
                cmd.Parameters.AddWithValue("m_type", packet.MsgType.ToString());
                cmd.Parameters.AddWithValue("d_type", packet.DataType.ToString());
                cmd.Parameters.AddWithValue("val", packet.Value);
                cmd.Parameters.AddWithValue("ts", time);
                
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB ERRO] Falha ao inserir: {ex.Message}");
            }
        }

        private static async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, totalRead, count - totalRead);
                if (read == 0) return 0; 
                totalRead += read;
            }
            return totalRead;
        }
    }
}