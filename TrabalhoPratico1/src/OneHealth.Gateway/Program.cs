#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OneHealth.Common;

namespace OneHealth.Gateway
{
    class SensorConfig
    {
        public uint Id { get; set; }
        public string Estado { get; set; } = "";
        public string Zona { get; set; } = "";
        public string Tipos { get; set; } = "";
        public DateTime LastSync { get; set; }
        public ConcurrentQueue<float> RecentValues { get; } = new();
        public override string ToString() => $"{Id}:{Estado}:{Zona}:{Tipos}:{LastSync:s}";
    }

    class Program
    {
        private static int SENSOR_TCP_PORT = 5001;
        private static int SENSOR_UDP_PORT = 6000;
        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_PORT = 5005; 

        private static readonly string CONFIG_FILE = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "sensors_config.csv"));
        private static readonly string VIDEO_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "videos"));
        
        private static readonly ConcurrentDictionary<uint, SensorConfig> _sensors = new();
        private static TcpClient? _serverClient;
        private static NetworkStream? _serverStream;
        private static readonly SemaphoreSlim _serverSemaphore = new(1, 1);

        // BUFFER DE DADOS NORMAIS (Lotes de 10)
        private static readonly List<TelemetryPacket> _normalBuffer = new();
        private static readonly object _bufferLock = new object();

        static async Task Main(string[] args)
        {
            if (args.Length > 0 && int.TryParse(args[0], out int portaBase)) {
                SENSOR_TCP_PORT = portaBase;
                SENSOR_UDP_PORT = portaBase + 999;
            }

            Console.Title = $"One Health - Edge Gateway ({SENSOR_TCP_PORT})";
            Console.WriteLine($"=== [GATEWAY DE BORDA TCP:{SENSOR_TCP_PORT} | UDP:{SENSOR_UDP_PORT}] ===");

            LoadConfig();
            await ConnectToServerAsync();

            _ = Task.Run(WatchdogTaskAsync);
            _ = Task.Run(DiskSyncTaskAsync);
            _ = Task.Run(StartUdpVideoProxyAsync); 

            TcpListener listener = new TcpListener(IPAddress.Any, SENSOR_TCP_PORT);
            listener.Start();
            Console.WriteLine($"[INFO] Operacional na porta TCP:{SENSOR_TCP_PORT} e UDP:{SENSOR_UDP_PORT}\n");

            while (true)
            {
                try
                {
                    TcpClient sensorClient = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleSensorAsync(sensorClient));
                }
                catch (Exception ex) { Console.WriteLine($"[ERRO REDE] {ex.Message}"); }
            }
        }
        
        private static async Task HandleSensorAsync(TcpClient client)
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
                        if (bytesRead == 0) break;

                        TelemetryPacket packet = TelemetryPacket.FromBytes(buffer);
                        if (!packet.IsValid()) continue;

                        if (!_sensors.TryGetValue(packet.SensorID, out SensorConfig? cfg))
                        {
                            Console.WriteLine($"[FIREWALL] Sensor {packet.SensorID} barrado.");
                            break; 
                        }

                        if (cfg.Estado == "manutencao") continue;

                        if (cfg.Estado == "desativado")
                        {
                            cfg.Estado = "ativo";
                            Console.WriteLine($"[INFO] Sensor {packet.SensorID} ressuscitou.");
                            await SendStatusToServerAsync(packet.SensorID, 1);
                        }

                        cfg.LastSync = DateTime.Now;

                        if (packet.MsgType == MsgType.DATA || packet.MsgType == MsgType.ALERT)
                        {
                            cfg.RecentValues.Enqueue(packet.Value);
                            while (cfg.RecentValues.Count > 10) cfg.RecentValues.TryDequeue(out _);
                            packet.Value = (float)cfg.RecentValues.Average();
                            packet.CalculateAndSetChecksum(); 
                        }

                        // LÓGICA DE PRIORIDADE E BATCH DE 10
                        if (packet.MsgType == MsgType.ALERT || packet.MsgType == MsgType.STATUS || packet.MsgType == MsgType.BYE || packet.MsgType == MsgType.HELO)
                        {
                            // FILA PRIORITÁRIA (Envio Imediato)
                            if (packet.MsgType == MsgType.ALERT) Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[PRIORIDADE] Encaminhamento direto de MsgType: {packet.MsgType}");
                            Console.ResetColor();
                            await ForwardToServerAsync(packet);
                        }
                        else if (packet.MsgType == MsgType.DATA)
                        {
                            // FILA NORMAL (Batching)
                            List<TelemetryPacket> batchToSend = null;
                            lock (_bufferLock)
                            {
                                _normalBuffer.Add(packet);
                                if (_normalBuffer.Count >= 10)
                                {
                                    batchToSend = new List<TelemetryPacket>(_normalBuffer);
                                    _normalBuffer.Clear();
                                }
                            }

                            if (batchToSend != null)
                            {
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine("[BUFFER] 10 pacotes de telemetria acumulados. A enviar lote para o Servidor...");
                                Console.ResetColor();
                                foreach (var p in batchToSend) await ForwardToServerAsync(p);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static async Task ConnectToServerAsync()
        {
            try
            {
                _serverClient = new TcpClient();
                await _serverClient.ConnectAsync(SERVER_IP, SERVER_PORT);
                _serverStream = _serverClient.GetStream();
                Console.WriteLine("[INFO] Ligado ao Servidor Central.");
            }
            catch (Exception ex) { Console.WriteLine($"[ERRO FATAL] {ex.Message}"); Environment.Exit(1); }
        }

        private static async Task ForwardToServerAsync(TelemetryPacket packet)
        {
            if (_serverStream == null) return;
            await _serverSemaphore.WaitAsync();
            try {
                byte[] bytes = packet.ToBytes();
                await _serverStream.WriteAsync(bytes, 0, bytes.Length);
            }
            finally { _serverSemaphore.Release(); }
        }

        private static async Task SendStatusToServerAsync(uint sensorId, float statusValue)
        {
            var packet = new TelemetryPacket { MsgType = MsgType.STATUS, DataType = DataType.Unknown, SensorID = sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Value = statusValue };
            packet.CalculateAndSetChecksum();
            await ForwardToServerAsync(packet);
        }

        private static async Task WatchdogTaskAsync()
        {
            while (true)
            {
                await Task.Delay(15000);
                var now = DateTime.Now;
                foreach (var sensor in _sensors.Values)
                {
                    if (sensor.Estado == "ativo" && (now - sensor.LastSync).TotalSeconds > 45)
                    {
                        sensor.Estado = "desativado";
                        Console.WriteLine($"[WATCHDOG] Sensor {sensor.Id} inativo. Estado: 'desativado'.");
                        await SendStatusToServerAsync(sensor.Id, 0); 
                    }
                }
            }
        }

        private static void LoadConfig()
        {
            if (!File.Exists(CONFIG_FILE)) return;
            foreach (var line in File.ReadAllLines(CONFIG_FILE))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(':');
                if (parts.Length >= 5 && uint.TryParse(parts[0], out uint id))
                {
                    _sensors[id] = new SensorConfig { Id = id, Estado = parts[1], Zona = parts[2], Tipos = parts[3], LastSync = DateTime.Now };
                }
            }
        }

        private static async Task DiskSyncTaskAsync()
        {
            while (true)
            {
                await Task.Delay(30000); 
                try {
                    await File.WriteAllLinesAsync(CONFIG_FILE, _sensors.Values.Select(s => s.ToString()).ToArray());
                } catch { }
            }
        }

        private static async Task StartUdpVideoProxyAsync()
        {
            if (!Directory.Exists(VIDEO_DIR)) Directory.CreateDirectory(VIDEO_DIR);
            using var udpClient = new UdpClient(SENSOR_UDP_PORT);
            while (true)
            {
                try
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();
                    if (result.Buffer.Length >= 16)
                    {
                        VideoPacketHeader header = VideoPacketHeader.FromBytes(result.Buffer);
                        byte[] payload = new byte[result.Buffer.Length - 16];
                        Buffer.BlockCopy(result.Buffer, 16, payload, 0, payload.Length);
                        string fileName = $"S{header.SensorID}_Recording.raw";
                        string filePath = Path.Combine(VIDEO_DIR, fileName);
                        using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
                        {
                            await fs.WriteAsync(payload, 0, payload.Length);
                        }
                        if (header.SequenceNum % 20 == 0) 
                            Console.WriteLine($"[VÍDEO GRAVADO] Sensor {header.SensorID} | Frame Seq: {header.SequenceNum} em {fileName}");
                    }
                }
                catch { }
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