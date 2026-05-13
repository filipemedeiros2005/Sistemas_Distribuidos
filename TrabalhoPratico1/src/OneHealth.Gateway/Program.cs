#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OneHealth.Common;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OneHealth.Gateway
{
    class SensorConfig
    {
        public uint Id { get; set; }
        public string Estado { get; set; } = "";
        public string Zona { get; set; } = "";
        public HashSet<DataType> AllowedTypes { get; } = new();
        public DateTime LastSync { get; set; }
    }

    class Program
    {
        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_PORT = 5005;

        private static int _gatewayId = 5001;
        private static int _sensorUdpPort = 6000;
        private static string _configPath = "";

        private static string _brokerHost = "localhost";
        private static int _brokerPort = 5672;
        private static string _brokerUser = "guest";
        private static string _brokerPass = "guest";

        private static readonly ConcurrentDictionary<uint, SensorConfig> _sensors = new();
        private static readonly HashSet<string> _subscribedZonas = new(StringComparer.OrdinalIgnoreCase);

        private static TcpClient? _serverClient;
        private static NetworkStream? _serverStream;

        private static readonly object _filaLock = new();
        private static readonly PriorityQueue<TelemetryPacket, int> _filaPrioridade = new();

        private static IConnection? _amqpConnection;
        private static IChannel? _amqpChannel;
        private static string _queueName = "";
        private static string _videoDir = "";

        private static Mutex _csvMutex = new(false, "OneHealthGwCsv");

        static async Task Main(string[] args)
        {
            ParseArgs(args);

            Console.Title = $"One Health - Edge Gateway #{_gatewayId}";
            Console.WriteLine($"=== [GATEWAY #{_gatewayId} | UDP:{_sensorUdpPort} | broker:{_brokerHost}:{_brokerPort}] ===");

            ResolveDataPaths();
            LoadConfig();
            await ConnectToServerAsync();
            await ConnectBrokerAndSubscribeAsync();

            _ = Task.Run(WatchdogTaskAsync);
            _ = Task.Run(CicloDeEnvioAoServidorAsync);
            _ = Task.Run(StartUdpVideoProxyAsync);
            _ = Task.Run(CsvWritebackTaskAsync);

            await Task.Delay(Timeout.Infinite);
        }

        private static void ParseArgs(string[] args)
        {
            if (args.Length > 0 && int.TryParse(args[0], out int id))
            {
                _gatewayId = id;
                _sensorUdpPort = id + 999;
            }
            for (int i = 1; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith("config=", StringComparison.OrdinalIgnoreCase)) _configPath = a.Substring(7);
                else if (a.StartsWith("broker=", StringComparison.OrdinalIgnoreCase))
                {
                    var hp = a.Substring(7).Split(':');
                    _brokerHost = hp[0];
                    if (hp.Length > 1 && int.TryParse(hp[1], out var p)) _brokerPort = p;
                }
                else if (!Path.GetExtension(a).Equals("", StringComparison.OrdinalIgnoreCase))
                {
                    _configPath = a;
                }
            }
        }

        private static void ResolveDataPaths()
        {
            string baseDir = AppContext.BaseDirectory;
            string dataRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "data"));
            if (!Directory.Exists(dataRoot))
                dataRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "data"));

            if (string.IsNullOrEmpty(_configPath))
                _configPath = Path.Combine(dataRoot, "gateway_configs", $"gw_{_gatewayId}.csv");

            _videoDir = Path.Combine(dataRoot, "videos");
        }

        private static void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[AVISO] Ficheiro de whitelist não encontrado em {_configPath}. Firewall em modo bypass.");
                Console.ResetColor();
                _subscribedZonas.Add("#");
                return;
            }

            foreach (var raw in File.ReadAllLines(_configPath))
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                var (id, estado, zona, types, _) = ParseConfigLine(line);
                if (id == 0) continue;

                var cfg = new SensorConfig
                {
                    Id = id,
                    Estado = estado,
                    Zona = zona,
                    LastSync = DateTime.UtcNow
                };
                foreach (var t in types) cfg.AllowedTypes.Add(t);

                _sensors[id] = cfg;
                if (!string.IsNullOrWhiteSpace(zona)) _subscribedZonas.Add(zona);
            }

            Console.WriteLine($"[CONFIG] {_sensors.Count} sensores autorizados, {_subscribedZonas.Count} zona(s) subscritas.");
            foreach (var s in _sensors.Values)
                Console.WriteLine($"        - S{s.Id} ({s.Estado}, {s.Zona}, [{string.Join(",", s.AllowedTypes)}])");
        }

        private static (uint id, string estado, string zona, List<DataType> types, DateTime lastSync) ParseConfigLine(string line)
        {
            var parts = line.Split(':');
            if (parts.Length < 5) return (0, "", "", new(), DateTime.MinValue);
            if (!uint.TryParse(parts[0], out var id)) return (0, "", "", new(), DateTime.MinValue);

            var estado = parts[1];
            var zona = parts[2];
            var typesRaw = parts[3].Trim('[', ']');
            var types = new List<DataType>();
            foreach (var t in typesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Enum.TryParse<DataType>(t.Trim(), true, out var dt)) types.Add(dt);
            }
            DateTime.TryParse(parts[4], out var lastSync);
            return (id, estado, zona, types, lastSync);
        }

        private static async Task ConnectBrokerAndSubscribeAsync()
        {
            var factory = new ConnectionFactory
            {
                HostName = _brokerHost,
                Port = _brokerPort,
                UserName = _brokerUser,
                Password = _brokerPass,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = $"OneHealthGateway-{_gatewayId}"
            };

            _amqpConnection = await factory.CreateConnectionAsync();
            _amqpChannel = await _amqpConnection.CreateChannelAsync();
            await _amqpChannel.ExchangeDeclareAsync(Topic.Exchange, ExchangeType.Topic, durable: false, autoDelete: false);

            _queueName = $"oh.gateway.{_gatewayId}";
            await _amqpChannel.QueueDeclareAsync(_queueName, durable: false, exclusive: false, autoDelete: true);

            foreach (var zona in _subscribedZonas)
            {
                string pattern = zona == "#" ? "#" : Topic.ZoneBindingPattern(zona);
                await _amqpChannel.QueueBindAsync(_queueName, Topic.Exchange, pattern);
                Console.WriteLine($"[BROKER] Bind {pattern} -> {_queueName}");
            }

            var consumer = new AsyncEventingBasicConsumer(_amqpChannel);
            consumer.ReceivedAsync += OnAmqpMessageAsync;
            await _amqpChannel.BasicConsumeAsync(_queueName, autoAck: true, consumer: consumer);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[BROKER] A consumir da queue {_queueName}.");
            Console.ResetColor();
        }

        private static async Task OnAmqpMessageAsync(object sender, BasicDeliverEventArgs ea)
        {
            try
            {
                var body = ea.Body.ToArray();
                if (body.Length < 20) return;

                var packet = TelemetryPacket.FromBytes(body);
                if (!packet.IsValid())
                {
                    Console.WriteLine($"[CHECKSUM] Pacote descartado de {ea.RoutingKey}.");
                    return;
                }

                string zona = "";
                if (!_sensors.IsEmpty)
                {
                    if (!_sensors.TryGetValue(packet.SensorID, out var cfg))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[FIREWALL] S{packet.SensorID} fora da whitelist — descartado.");
                        Console.ResetColor();
                        return;
                    }
                    if (cfg.Estado == "manutencao") return;

                    if (packet.MsgType == MsgType.DATA || packet.MsgType == MsgType.ALERT)
                    {
                        if (cfg.AllowedTypes.Count > 0 && !cfg.AllowedTypes.Contains(packet.DataType))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine($"[FIREWALL] S{packet.SensorID}/{packet.DataType} não autorizado — descartado.");
                            Console.ResetColor();
                            return;
                        }
                    }

                    cfg.LastSync = DateTime.UtcNow;
                    zona = cfg.Zona;
                    if (cfg.Estado.Contains("desativ"))
                    {
                        cfg.Estado = "ativo";
                        EnqueueServerStatus(cfg.Id, 1);
                    }
                }

                if (packet.MsgType == MsgType.DATA || packet.MsgType == MsgType.ALERT)
                {
                    var result = await PreprocessorClient.NormalizeAsync(
                        packet.SensorID,
                        packet.DataType.ToString().ToUpperInvariant(),
                        packet.Value,
                        packet.TimeStamp,
                        zona);

                    if (result == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[PREPROC] Indisponível — S{packet.SensorID}/{packet.DataType} descartado (fail-closed).");
                        Console.ResetColor();
                        return;
                    }
                    if (result.Dropped)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[PREPROC] Drop S{packet.SensorID}/{packet.DataType}={packet.Value:F2} ({result.DropReason}).");
                        Console.ResetColor();
                        return;
                    }

                    if (Math.Abs(result.Value - packet.Value) > 1e-6f)
                    {
                        Console.WriteLine($"[PREPROC] Normalize S{packet.SensorID}/{packet.DataType} {packet.Value:F2} -> {result.Value:F2}");
                        packet.Value = (float)result.Value;
                    }
                }

                int prio = (packet.MsgType == MsgType.ALERT || packet.MsgType == MsgType.BYE) ? 0 : 1;
                lock (_filaLock) _filaPrioridade.Enqueue(packet, prio);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO CONSUMER] {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void EnqueueServerStatus(uint sensorId, float value)
        {
            var pkt = new TelemetryPacket
            {
                MsgType = MsgType.STATUS,
                SensorID = sensorId,
                Value = value,
                TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            lock (_filaLock) _filaPrioridade.Enqueue(pkt, 0);
        }

        private static async Task CicloDeEnvioAoServidorAsync()
        {
            while (true)
            {
                try
                {
                    if (_serverStream == null)
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    TelemetryPacket pacote = default;
                    bool temPacote = false;
                    lock (_filaLock)
                    {
                        if (_filaPrioridade.Count > 0)
                        {
                            pacote = _filaPrioridade.Dequeue();
                            temPacote = true;
                        }
                    }

                    if (temPacote)
                    {
                        pacote.CalculateAndSetChecksum();
                        await _serverStream.WriteAsync(pacote.ToBytes());
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO DESPACHANTE] {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        }

        private static async Task ConnectToServerAsync()
        {
            try
            {
                _serverClient = new TcpClient();
                await _serverClient.ConnectAsync(SERVER_IP, SERVER_PORT);
                _serverStream = _serverClient.GetStream();
                Console.WriteLine($"[SERVER] Ligado ao Servidor Central {SERVER_IP}:{SERVER_PORT}.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERRO SERVER] Não foi possível ligar ao Servidor Central: {ex.Message}");
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        private static async Task WatchdogTaskAsync()
        {
            while (true)
            {
                await Task.Delay(15_000);
                var now = DateTime.UtcNow;
                foreach (var sensor in _sensors.Values)
                {
                    if (sensor.Estado == "ativo" && (now - sensor.LastSync).TotalSeconds > 45)
                    {
                        sensor.Estado = "desativado";
                        EnqueueServerStatus(sensor.Id, 0);
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"[WATCHDOG] S{sensor.Id} sem atividade há >45s — marcado como desativado.");
                        Console.ResetColor();
                    }
                }
            }
        }

        private static async Task CsvWritebackTaskAsync()
        {
            while (true)
            {
                await Task.Delay(30_000);
                if (string.IsNullOrEmpty(_configPath) || !_sensors.Any()) continue;

                if (!_csvMutex.WaitOne(TimeSpan.FromSeconds(5))) continue;
                try
                {
                    var sb = new StringBuilder();
                    foreach (var s in _sensors.Values.OrderBy(s => s.Id))
                    {
                        string types = string.Join(",", s.AllowedTypes);
                        string ts = s.LastSync == DateTime.MinValue
                            ? DateTime.UtcNow.ToString("o")
                            : s.LastSync.ToString("o");
                        sb.AppendLine($"{s.Id}:{s.Estado}:{s.Zona}:[{types}]:{ts}");
                    }
                    File.WriteAllText(_configPath, sb.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO CSV] {ex.Message}");
                }
                finally
                {
                    _csvMutex.ReleaseMutex();
                }
            }
        }

        private static async Task StartUdpVideoProxyAsync()
        {
            if (!Directory.Exists(_videoDir)) Directory.CreateDirectory(_videoDir);
            else
            {
                foreach (var file in Directory.GetFiles(_videoDir)) File.Delete(file);
            }

            using var udpClient = new UdpClient(_sensorUdpPort);
            using var serverForwarder = new UdpClient();

            Console.WriteLine($"[VIDEO] À escuta UDP na porta {_sensorUdpPort}.");

            while (true)
            {
                try
                {
                    var res = await udpClient.ReceiveAsync();
                    if (res.Buffer.Length < 16) continue;

                    var header = VideoPacketHeader.FromBytes(res.Buffer);

                    byte[] payload = new byte[res.Buffer.Length - 16];
                    Buffer.BlockCopy(res.Buffer, 16, payload, 0, payload.Length);

                    using (var fs = new FileStream(
                        Path.Combine(_videoDir, $"S{header.SensorID}_Recording.raw"),
                        FileMode.Append, FileAccess.Write, FileShare.None))
                    {
                        await fs.WriteAsync(payload);
                    }

                    await serverForwarder.SendAsync(res.Buffer, res.Buffer.Length, SERVER_IP, 7000);

                    if (header.SequenceNum % 10 == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[VÍDEO EDGE] Frame {header.SequenceNum} (S{header.SensorID}) guardado e reencaminhado.");
                        Console.ResetColor();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO VIDEO] {ex.Message}");
                }
            }
        }
    }
}
