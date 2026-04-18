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
    // Estrutura em RAM para a Whitelist
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
        private const int SENSOR_TCP_PORT = 5001;
        private const int SENSOR_UDP_PORT = 6000;
        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_PORT = 5005; 

        // Garante que tens ESTAS DUAS linhas aqui:
        private static readonly string CONFIG_FILE = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "sensors_config.csv"));
        private static readonly string VIDEO_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "videos"));
        // Dicionário de Configurações e Estado (RAM)
        private static readonly ConcurrentDictionary<uint, SensorConfig> _sensors = new();

        // O "Cano Único" para o Servidor Central (Multiplexagem)
        private static TcpClient? _serverClient;
        private static NetworkStream? _serverStream;
        private static readonly SemaphoreSlim _serverSemaphore = new(1, 1); // Mutex assíncrono para garantir envio ordenado

        static async Task Main(string[] args)
        {
            Console.Title = "One Health - Edge Gateway";
            Console.WriteLine("=== [GATEWAY DE BORDA ONE HEALTH] ===");

            // 1. Carregar a Whitelist para a Memória
            LoadConfig();

            // 2. Ligar ao Servidor Central (Persistente)
            await ConnectToServerAsync();

            // 3. Iniciar Tarefas de Fundo (Edge Computing)
            _ = Task.Run(WatchdogTaskAsync);
            _ = Task.Run(DiskSyncTaskAsync);
            _ = Task.Run(StartUdpVideoProxyAsync); // Canal Multimédia

            // 4. Iniciar Listener TCP para os Sensores
            TcpListener listener = new TcpListener(IPAddress.Any, SENSOR_TCP_PORT);
            listener.Start();
            Console.WriteLine($"[INFO] Gateway operacional. A ouvir Sensores em TCP:{SENSOR_TCP_PORT} e UDP:{SENSOR_UDP_PORT}\n");

            while (true)
            {
                try
                {
                    TcpClient sensorClient = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleSensorAsync(sensorClient));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO REDE] Falha ao aceitar sensor: {ex.Message}");
                }
            }
        }
        
        // LÓGICA DE PROCESSAMENTO DE SENSORES (TCP)

        private static async Task HandleSensorAsync(TcpClient client)
        {
            string endpoint = client.Client.RemoteEndPoint?.ToString() ?? "Desconhecido";
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

                        // Integridade Básica
                        if (!packet.IsValid()) continue;

                        // 1. FIREWALL / WHITELIST
                        if (!_sensors.TryGetValue(packet.SensorID, out SensorConfig? cfg))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"[FIREWALL] Sensor {packet.SensorID} desconhecido. Pacote descartado.");
                            Console.ResetColor();
                            break; // Corta a ligação imediatamente
                        }

                        if (cfg.Estado == "manutencao")
                        {
                            // Ignora silenciosamente, não atualiza o LastSync
                            continue;
                        }

                        // Reativa o sensor se ele estava 'desativado' pelo Watchdog
                        if (cfg.Estado == "desativado")
                        {
                            cfg.Estado = "ativo";
                            Console.WriteLine($"[INFO] Sensor {packet.SensorID} recuperou a ligação. Estado: ATIVO.");
                            await SendStatusToServerAsync(packet.SensorID, 1); // 1 = Ativo
                        }

                        // Atualiza a vida do Sensor na RAM
                        cfg.LastSync = DateTime.Now;

                        // 2. AGREGAÇÃO E EDGE COMPUTING (Fila Circular)
                        if (packet.MsgType == MsgType.DATA || packet.MsgType == MsgType.ALERT)
                        {
                            cfg.RecentValues.Enqueue(packet.Value);
                            
                            // Mantém apenas os últimos 10 valores
                            while (cfg.RecentValues.Count > 10)
                            {
                                cfg.RecentValues.TryDequeue(out _);
                            }

                            // Sobrescreve o valor do pacote com a Média Agregada
                            float average = (float)cfg.RecentValues.Average();
                            packet.Value = average;
                            packet.CalculateAndSetChecksum(); // Recalcula para o servidor aceitar
                        }

                        // 3. LOG LOCAL E FORWARDING
                        if (packet.MsgType == MsgType.HELO)
                        {
                            Console.WriteLine($"[HANDSHAKE] Sensor {packet.SensorID} autenticado com sucesso.");
                        }
                        else if (packet.MsgType == MsgType.ALERT)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[EMERGÊNCIA] Sensor {packet.SensorID} disparou ALERTA! Reencaminhando fluxo...");
                            Console.ResetColor();
                        }
                        else if (packet.MsgType == MsgType.BYE)
                        {
                            Console.WriteLine($"[INFO] Sensor {packet.SensorID} encerrou a sessão de forma limpa.");
                            break;
                        }

                        // Envia para o Servidor Central
                        await ForwardToServerAsync(packet);
                    }
                }
                catch (Exception)
                {
                    // Sensor desconectado abruptamente. O Watchdog tratará disto.
                }
            }
        }
        
        // MULTIPLEXAGEM PARA O SERVIDOR CENTRAL

        private static async Task ConnectToServerAsync()
        {
            try
            {
                _serverClient = new TcpClient();
                await _serverClient.ConnectAsync(SERVER_IP, SERVER_PORT);
                _serverStream = _serverClient.GetStream();
                Console.WriteLine("[INFO] Ligação persistente ao Servidor Central estabelecida.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO FATAL] Não foi possível ligar ao Servidor Central: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static async Task ForwardToServerAsync(TelemetryPacket packet)
        {
            if (_serverStream == null) return;

            // Bloqueio assíncrono para garantir que pacotes de sensores diferentes não se misturam nos bytes
            await _serverSemaphore.WaitAsync();
            try
            {
                byte[] bytes = packet.ToBytes();
                await _serverStream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO FORWARD] Falha ao comunicar com o servidor: {ex.Message}");
            }
            finally
            {
                _serverSemaphore.Release();
            }
        }

        private static async Task SendStatusToServerAsync(uint sensorId, float statusValue)
        {
            var packet = new TelemetryPacket
            {
                MsgType = MsgType.STATUS,
                DataType = DataType.Unknown,
                SensorID = sensorId,
                TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Value = statusValue // 0 = Inativo, 1 = Ativo
            };
            packet.CalculateAndSetChecksum();
            await ForwardToServerAsync(packet);
        }
        
        // WATCHDOG (Gestão de Inativos)

        private static async Task WatchdogTaskAsync()
        {
            while (true)
            {
                await Task.Delay(15000); // Verifica a cada 15 segundos

                var now = DateTime.Now;
                foreach (var sensor in _sensors.Values)
                {
                    if (sensor.Estado == "ativo" && (now - sensor.LastSync).TotalSeconds > 45)
                    {
                        sensor.Estado = "desativado";
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[WATCHDOG] Sensor {sensor.Id} inativo (>45s). Estado alterado para 'desativado'.");
                        Console.ResetColor();

                        // Avisa o servidor central da "morte" do sensor
                        await SendStatusToServerAsync(sensor.Id, 0); 
                    }
                }
            }
        }

        // GESTÃO DE DISCO (I/O Decoupling)

        private static void LoadConfig()
        {
            if (!File.Exists(CONFIG_FILE))
            {
                Console.WriteLine($"[AVISO] Ficheiro CSV não encontrado em {CONFIG_FILE}. A operar sem sensores.");
                return;
            }

            var lines = File.ReadAllLines(CONFIG_FILE);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(':');
                if (parts.Length >= 5 && uint.TryParse(parts[0], out uint id))
                {
                    _sensors[id] = new SensorConfig
                    {
                        Id = id,
                        Estado = parts[1],
                        Zona = parts[2],
                        Tipos = parts[3],
                        LastSync = DateTime.TryParse(parts[4] + ":" + parts[5] + ":" + parts[6], out DateTime dt) ? dt : DateTime.Now
                    };
                }
            }
            Console.WriteLine($"[INFO] Whitelist carregada: {_sensors.Count} sensores reconhecidos.");
        }

        private static async Task DiskSyncTaskAsync()
        {
            while (true)
            {
                await Task.Delay(30000); // Grava no disco a cada 30 segundos
                
                try
                {
                    var lines = _sensors.Values.Select(s => s.ToString()).ToArray();
                    await File.WriteAllLinesAsync(CONFIG_FILE, lines);
                    // Console.WriteLine("[SISTEMA] CSV sincronizado com sucesso."); // Descomentar para debug
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO I/O] Falha ao sincronizar CSV: {ex.Message}");
                }
            }
        }
        
        // PROXY DE VÍDEO UDP (Opcional/Live Stream)

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
                        
                        // Extrair apenas a imagem (Payload) do pacote UDP
                        byte[] payload = new byte[result.Buffer.Length - 16];
                        Buffer.BlockCopy(result.Buffer, 16, payload, 0, payload.Length);

                        // Gravar de forma contínua no disco (Append)
                        string fileName = $"S{header.SensorID}_Recording.raw";
                        string filePath = Path.Combine(VIDEO_DIR, fileName);

                        using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
                        {
                            await fs.WriteAsync(payload, 0, payload.Length);
                        }

                        if (header.SequenceNum % 20 == 0) 
                        {
                            Console.WriteLine($"[VÍDEO GRAVADO] Sensor {header.SensorID} | Frame Seq: {header.SequenceNum} guardado em {fileName}");
                        }
                    }
                }
                catch { /* Ignora erros de rede UDP */ }
            }
        }

        // UTILITÁRIO

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