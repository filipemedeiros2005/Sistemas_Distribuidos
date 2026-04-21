#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OneHealth.Common;

namespace OneHealth.Gateway
{
    class SensorConfig {
        public uint Id { get; set; } public string Estado { get; set; } = "";
        public DateTime LastSync { get; set; }
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

        private static readonly object _filaLock = new();
        private static readonly PriorityQueue<TelemetryPacket, int> _filaPrioridade = new();

        static async Task Main(string[] args)
        {
            if (args.Length > 0 && int.TryParse(args[0], out int portaBase)) {
                SENSOR_TCP_PORT = portaBase; SENSOR_UDP_PORT = portaBase + 999;
            }

            Console.Title = $"One Health - Edge Gateway ({SENSOR_TCP_PORT})";
            Console.WriteLine($"=== [GATEWAY TCP:{SENSOR_TCP_PORT} | UDP:{SENSOR_UDP_PORT}] ===");

            LoadConfig();
            await ConnectToServerAsync();

            _ = Task.Run(WatchdogTaskAsync);
            _ = Task.Run(CicloDeEnvioAoServidorAsync); 
            _ = Task.Run(StartUdpVideoProxyAsync); 

            TcpListener listener = new TcpListener(IPAddress.Any, SENSOR_TCP_PORT);
            listener.Start();

            while (true)
            {
                try {
                    TcpClient sensorClient = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleSensorAsync(sensorClient));
                } catch { }
            }
        }
        
        private static async Task HandleSensorAsync(TcpClient client)
        {
            using (client) using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[20];
                while (true)
                {
                    if (await ReadExactlyAsync(stream, buffer, 20) == 0) break;

                    TelemetryPacket packet = TelemetryPacket.FromBytes(buffer);
                    if (!packet.IsValid()) continue;


                    if (!_sensors.IsEmpty) {
                        if (!_sensors.TryGetValue(packet.SensorID, out SensorConfig? cfg) || cfg.Estado == "manutencao") break;
                        
                        if (cfg.Estado.Contains("desativ")) { 
                            cfg.Estado = "ativo";
                            lock (_filaLock) _filaPrioridade.Enqueue(new TelemetryPacket { MsgType = MsgType.STATUS, SensorID = packet.SensorID, Value = 1, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, 0);
                        }
                        cfg.LastSync = DateTime.Now;
                    }

                    if (packet.MsgType == MsgType.ALERT || packet.MsgType == MsgType.HELO || packet.MsgType == MsgType.BYE)
                        lock (_filaLock) _filaPrioridade.Enqueue(packet, 0); 
                    else
                        lock (_filaLock) _filaPrioridade.Enqueue(packet, 1); 
                }
            }
        }

        private static async Task CicloDeEnvioAoServidorAsync()
        {
            while (true)
            {
                try {
                    if (_serverStream != null) {
                        bool temPacote = false;
                        TelemetryPacket pacoteParaEnviar = default;

                        lock (_filaLock) {
                            if (_filaPrioridade.Count > 0) {
                                pacoteParaEnviar = _filaPrioridade.Dequeue();
                                temPacote = true;
                            }
                        }

                        if (temPacote) {
                            await _serverStream.WriteAsync(pacoteParaEnviar.ToBytes());
                        }
                        else {
                            await Task.Delay(10);
                        }
                    }
                } catch (Exception ex) { Console.WriteLine("[ERRO DESPACHANTE] " + ex.Message); await Task.Delay(1000); }
            }
        }

        private static async Task ConnectToServerAsync() {
            try { _serverClient = new TcpClient(); await _serverClient.ConnectAsync(SERVER_IP, SERVER_PORT); _serverStream = _serverClient.GetStream(); } catch { Environment.Exit(1); }
        }
        
        private static async Task WatchdogTaskAsync() {
            while (true) {
                await Task.Delay(15000); var now = DateTime.Now;
                foreach (var sensor in _sensors.Values) {
                    if (sensor.Estado == "ativo" && (now - sensor.LastSync).TotalSeconds > 45) {
                        sensor.Estado = "desativado";
                        lock (_filaLock) _filaPrioridade.Enqueue(new TelemetryPacket { MsgType = MsgType.STATUS, SensorID = sensor.Id, Value = 0, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, 0);
                    }
                }
            }
        }
        
        private static void LoadConfig() {
            if (!File.Exists(CONFIG_FILE)) { Console.WriteLine("[AVISO] CSV de autorização não encontrado. Firewall em modo Bypass (Aceita tudo)."); return; }
            foreach (var line in File.ReadAllLines(CONFIG_FILE)) {
                var parts = line.Split(':');
                if (parts.Length >= 5 && uint.TryParse(parts[0], out uint id)) _sensors[id] = new SensorConfig { Id = id, Estado = parts[1], LastSync = DateTime.Now };
            }
        }
        
        private static async Task StartUdpVideoProxyAsync() {
            if (!Directory.Exists(VIDEO_DIR)) Directory.CreateDirectory(VIDEO_DIR);
            else {
                foreach(var file in Directory.GetFiles(VIDEO_DIR)) File.Delete(file);
            }
            
            using var udpClient = new UdpClient(SENSOR_UDP_PORT);
            using var serverForwarder = new UdpClient(); 

            while (true) {
                try {
                    var res = await udpClient.ReceiveAsync();
                    if (res.Buffer.Length >= 16) {
                        VideoPacketHeader header = VideoPacketHeader.FromBytes(res.Buffer);
                        

                        byte[] p = new byte[res.Buffer.Length - 16]; 
                        Buffer.BlockCopy(res.Buffer, 16, p, 0, p.Length);
                        using var fs = new FileStream(Path.Combine(VIDEO_DIR, $"S{header.SensorID}_Recording.raw"), FileMode.Append, FileAccess.Write, FileShare.None);
                        await fs.WriteAsync(p);


                        await serverForwarder.SendAsync(res.Buffer, res.Buffer.Length, SERVER_IP, 7000);

                        if (header.SequenceNum % 10 == 0) {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"[VÍDEO EDGE] Frame {header.SequenceNum} (S{header.SensorID}) guardado e reencaminhado para o Servidor!");
                            Console.ResetColor();
                        }
                    }
                } catch { }
            }
        }
        
        private static async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int count) {
            int t = 0; while (t < count) { int r = await stream.ReadAsync(buffer, t, count - t); if (r == 0) return 0; t += r; } return t;
        }
    }
}