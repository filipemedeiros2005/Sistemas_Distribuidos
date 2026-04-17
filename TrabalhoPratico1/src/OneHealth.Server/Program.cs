#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using OneHealth.Common;

namespace OneHealth.Server
{
    class Program
    {
        // Alterado de 5000 para 5005 para contornar a restrição do AirPlay no macOS
        private const int SERVER_PORT = 5005;
        
        // Caminho seguro para a pasta data (funciona quer corras pelo terminal ou pelo Rider)
        private static readonly string LOGS_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "server_logs"));
        
        private static readonly ConcurrentDictionary<DataType, object> _fileLocks = new();

        static async Task Main(string[] args)
        {
            Console.Title = "One Health - Central Server";
            Console.WriteLine("=== [SERVIDOR CENTRAL ONE HEALTH] ===");

            try 
            {
                if (!Directory.Exists(LOGS_DIR))
                {
                    Directory.CreateDirectory(LOGS_DIR);
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine($"[AVISO] Não foi possível criar a pasta em {LOGS_DIR}. Erro: {ex.Message}");
            }
            
            TcpListener listener = new TcpListener(IPAddress.Any, SERVER_PORT);
            listener.Start();
            Console.WriteLine($"[INFO] A ouvir Gateways na porta TCP {SERVER_PORT}...\n");

            while (true)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    
                    // Proteção contra Null Reference Types (Avisos CS8600/CS8602)
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

        private static async Task HandleGatewayAsync(TcpClient client, string endpoint)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[20]; 

                try
                {
                    while (true)
                    {
                        int bytesRead = await ReadExactlyAsync(stream, buffer, 20);
                        if (bytesRead == 0) 
                        {
                            Console.WriteLine($"[REDE] Gateway {endpoint} desconectado graciosamente.");
                            break;
                        }

                        TelemetryPacket packet = TelemetryPacket.FromBytes(buffer);

                        if (!packet.IsValid())
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine($"[SECURITY DROP] Pacote corrompido do Sensor {packet.SensorID}. Descartado.");
                            Console.ResetColor();
                            continue; 
                        }

                        ProcessPacket(packet);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO GATEWAY {endpoint}] Conexão perdida: {ex.Message}");
                }
            }
        }

        private static void ProcessPacket(TelemetryPacket packet)
        {
            string logEntry = $"[{DateTimeOffset.FromUnixTimeSeconds(packet.TimeStamp).LocalDateTime:yyyy-MM-dd HH:mm:ss}] SensorID: {packet.SensorID} | Msg: {packet.MsgType} | Valor: {packet.Value:F2}";

            switch (packet.MsgType)
            {
                case MsgType.ALERT:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"⚠️ [ALERTA CRÍTICO] {logEntry}");
                    Console.ResetColor();
                    SaveToFile(packet.DataType, logEntry);
                    break;

                case MsgType.STATUS:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    string status = packet.Value == 0 ? "INATIVO (Timeout/Desconectado)" : "ATIVO";
                    Console.WriteLine($"ℹ️  [STATUS UPDATE] Sensor {packet.SensorID} reportado como {status} pelo Watchdog.");
                    Console.ResetColor();
                    SaveToFile(DataType.Unknown, $"[STATUS] Sensor {packet.SensorID} -> {status}");
                    break;

                case MsgType.DATA:
                    Console.WriteLine($"[TELEMETRIA] {logEntry}");
                    SaveToFile(packet.DataType, logEntry);
                    break;

                case MsgType.BYE:
                    Console.WriteLine($"[DESCONEXÃO] Sensor {packet.SensorID} enviou sinal de saída (BYE).");
                    break;
            }
        }

        private static void SaveToFile(DataType type, string logLine)
        {
            string fileName = type == DataType.Unknown ? "SystemEvents.log" : $"{type}.log";
            string filePath = Path.Combine(LOGS_DIR, fileName);

            object fileLock = _fileLocks.GetOrAdd(type, _ => new object());

            lock (fileLock)
            {
                try
                {
                    File.AppendAllText(filePath, logLine + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO I/O] Falha ao escrever no log {fileName}: {ex.Message}");
                }
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