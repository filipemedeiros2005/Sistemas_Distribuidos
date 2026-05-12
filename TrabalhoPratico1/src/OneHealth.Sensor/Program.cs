#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OneHealth.Common;
using RabbitMQ.Client;

namespace OneHealth.Sensor
{
    class Program
    {
        private static uint _sensorId = 101;
        private static string _zona = "ZONA_DESCONHECIDA";
        private static string _brokerHost = "localhost";
        private static int _brokerPort = 5672;
        private static string _brokerUser = "guest";
        private static string _brokerPass = "guest";

        private const string GATEWAY_IP = "127.0.0.1";
        private static int _gatewayUdpPort = 6000;

        private static bool _isRunning = true;
        private static bool _isStreamingVideo = false;
        private static IConnection? _amqpConnection;
        private static IChannel? _amqpChannel;

        private static readonly List<TelemetryPacket> _bufferRotina = new();
        private static readonly ConcurrentQueue<byte[]> _videoBuffer = new();
        private static readonly int MAX_FRAMES_IN_BUFFER = 300;
        private static readonly Random _rng = new();

        private static readonly Dictionary<uint, string> ZonaPorSensor = new()
        {
            { 101, "ZONA_NORTE" },
            { 102, "ZONA_SUL" },
            { 103, "ZONA_INDUSTRIAL" },
            { 104, "ZONA_CENTRO" },
            { 999, "ZONA_ESPECIAL" }
        };

        static async Task Main(string[] args)
        {
            if (args.Length > 0 && uint.TryParse(args[0], out uint parsedId)) _sensorId = parsedId;
            string modoOperacao = args.Length > 1 ? args[1].ToLowerInvariant() : "auto";

            for (int i = 2; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith("zona=", StringComparison.OrdinalIgnoreCase)) _zona = a.Substring(5);
                else if (a.StartsWith("broker=", StringComparison.OrdinalIgnoreCase))
                {
                    var hp = a.Substring(7).Split(':');
                    _brokerHost = hp[0];
                    if (hp.Length > 1 && int.TryParse(hp[1], out var p)) _brokerPort = p;
                }
                else if (a.StartsWith("udp=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(a.Substring(4), out var u)) _gatewayUdpPort = u;
                }
                else if (int.TryParse(a, out var portaGw) && portaGw is >= 5000 and <= 5999)
                {
                    _gatewayUdpPort = portaGw + 999;
                }
            }

            if (_zona == "ZONA_DESCONHECIDA" && ZonaPorSensor.TryGetValue(_sensorId, out var zonaPorDefeito))
                _zona = zonaPorDefeito;

            Console.WriteLine($"=== [SENSOR {_sensorId} | {_zona}] a publicar em amqp://{_brokerHost}:{_brokerPort} ===");
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; _isRunning = false; };

            _ = Task.Run(BackgroundVideoCaptureTask);

            try
            {
                await ConnectBrokerAsync();
                await PublishAsync(new TelemetryPacket
                {
                    MsgType = MsgType.STATUS,
                    DataType = DataType.Unknown,
                    SensorID = _sensorId,
                    TimeStamp = NowUnix(),
                    Value = 1f
                });

                _ = Task.Run(HeartbeatLoopAsync);

                if (modoOperacao == "manual") await RunManualModeAsync();
                else await RunAutoSimulationAsync();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERRO BROKER] {ex.GetType().Name}: {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                _isRunning = false;
                try
                {
                    await PublishAsync(new TelemetryPacket
                    {
                        MsgType = MsgType.BYE,
                        SensorID = _sensorId,
                        TimeStamp = NowUnix()
                    });
                }
                catch { }
                if (_amqpChannel != null) await _amqpChannel.CloseAsync();
                if (_amqpConnection != null) await _amqpConnection.CloseAsync();
            }
        }

        private static async Task ConnectBrokerAsync()
        {
            var factory = new ConnectionFactory
            {
                HostName = _brokerHost,
                Port = _brokerPort,
                UserName = _brokerUser,
                Password = _brokerPass,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = $"OneHealthSensor-{_sensorId}"
            };

            _amqpConnection = await factory.CreateConnectionAsync();
            _amqpChannel = await _amqpConnection.CreateChannelAsync();
            await _amqpChannel.ExchangeDeclareAsync(
                exchange: Topic.Exchange,
                type: ExchangeType.Topic,
                durable: false,
                autoDelete: false);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[BROKER] Ligado ao exchange '{Topic.Exchange}' (topic).");
            Console.ResetColor();
        }

        private static async Task RunAutoSimulationAsync()
        {
            string baseDir = AppContext.BaseDirectory;
            string pathRaiz = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "data", "simulation", $"sensor_{_sensorId}.csv"));
            string pathSrc = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "data", "simulation", $"sensor_{_sensorId}.csv"));
            string csvPath = File.Exists(pathRaiz) ? pathRaiz : (File.Exists(pathSrc) ? pathSrc : "");

            if (string.IsNullOrEmpty(csvPath))
            {
                Console.WriteLine("[ERRO FATAL] CSV de simulação não encontrado. A parar.");
                return;
            }

            Console.WriteLine("[INFO] Algoritmo 3-Sigma Iniciado.");
            var linhas = await File.ReadAllLinesAsync(csvPath);

            var emaDict = new Dictionary<DataType, float>();
            var emaVarDict = new Dictionary<DataType, float>();
            var leiturasCountDict = new Dictionary<DataType, int>();
            float alpha = 0.2f;

            while (_isRunning)
            {
                foreach (var linha in linhas)
                {
                    if (!_isRunning) break;
                    if (string.IsNullOrWhiteSpace(linha) || !linha.Contains(',')) continue;
                    var p = linha.Split(',');
                    string valorFormatado = p[1].Replace(',', '.');

                    if (!Enum.TryParse(p[0], out DataType tipo)) continue;
                    if (!float.TryParse(valorFormatado, NumberStyles.Any, CultureInfo.InvariantCulture, out float rawValue)) continue;

                    if (!emaDict.ContainsKey(tipo))
                    {
                        emaDict[tipo] = BaselineFor(tipo, rawValue);
                        emaVarDict[tipo] = 1f;
                        leiturasCountDict[tipo] = 2;
                    }
                    leiturasCountDict[tipo]++;

                    float baseThreshold = BaseThresholdFor(tipo);
                    float desvioPadrao = (float)Math.Sqrt(emaVarDict[tipo]);
                    float threshold = Math.Max(3 * desvioPadrao, baseThreshold);
                    bool isAlerta = Math.Abs(rawValue - emaDict[tipo]) > threshold && leiturasCountDict[tipo] > 2;

                    var packet = new TelemetryPacket
                    {
                        MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA,
                        DataType = tipo,
                        SensorID = _sensorId,
                        TimeStamp = NowUnix(),
                        Value = rawValue
                    };

                    if (isAlerta)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[ALERTA 3-SIGMA] Pico de {rawValue:F2} ({tipo}) detetado!");
                        Console.ResetColor();
                        TriggerEmergencyVideo();

                        foreach (var pkt in _bufferRotina) await PublishAsync(pkt);
                        _bufferRotina.Clear();
                        await PublishAsync(packet);

                        emaDict[tipo] = rawValue;
                        emaVarDict[tipo] = 1f;
                        leiturasCountDict[tipo] = 0;
                    }
                    else
                    {
                        float delta = rawValue - emaDict[tipo];
                        emaDict[tipo] = emaDict[tipo] + alpha * delta;
                        emaVarDict[tipo] = alpha * delta * delta + (1.0f - alpha) * emaVarDict[tipo];

                        _bufferRotina.Add(packet);
                        Console.WriteLine($"[DADO NORMAL] {tipo}: {rawValue:F2} -> Buffer ({_bufferRotina.Count}/10)");
                        if (_bufferRotina.Count >= 10)
                        {
                            foreach (var pkt in _bufferRotina) await PublishAsync(pkt);
                            _bufferRotina.Clear();
                            Console.WriteLine("[BATCH ENVIADO] Lote de 10 pacotes despachado.");
                        }
                    }

                    await Task.Delay(5000);
                }
            }
        }

        private static async Task RunManualModeAsync()
        {
            Console.WriteLine("\n=== [ MODO MANUAL INICIADO ] ===");
            Console.WriteLine("Insira os dados separando o tipo do valor com um espaço.");
            Console.WriteLine("Exemplo: Temp 35.5");
            Console.WriteLine("Tipos permitidos: Temp, Hum, Ruido, PM10, PM25, Lum");

            var emaDict = new Dictionary<DataType, float>();
            var emaVarDict = new Dictionary<DataType, float>();
            var leiturasCountDict = new Dictionary<DataType, int>();
            float alpha = 0.2f;

            while (_isRunning)
            {
                Console.Write("\n> ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                var parts = input.Split(' ');
                if (parts.Length != 2 || !Enum.TryParse(parts[0], true, out DataType tipo))
                {
                    Console.WriteLine("[ERRO] Formato inválido. Use 'Tipo Valor'.");
                    continue;
                }
                string valFormatado = parts[1].Replace(',', '.');
                if (!float.TryParse(valFormatado, NumberStyles.Any, CultureInfo.InvariantCulture, out float rawValue))
                {
                    Console.WriteLine("[ERRO] O valor inserido não é um número válido.");
                    continue;
                }

                if (!emaDict.ContainsKey(tipo))
                {
                    emaDict[tipo] = BaselineFor(tipo, rawValue);
                    emaVarDict[tipo] = 1f;
                    leiturasCountDict[tipo] = 2;
                }
                leiturasCountDict[tipo]++;

                float baseThreshold = BaseThresholdFor(tipo);
                float desvioPadrao = (float)Math.Sqrt(emaVarDict[tipo]);
                float threshold = Math.Max(3 * desvioPadrao, baseThreshold);
                bool isAlerta = Math.Abs(rawValue - emaDict[tipo]) > threshold && leiturasCountDict[tipo] > 2;

                var packet = new TelemetryPacket
                {
                    MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA,
                    DataType = tipo,
                    SensorID = _sensorId,
                    TimeStamp = NowUnix(),
                    Value = rawValue
                };

                if (isAlerta)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ALERTA 3-SIGMA MANUAL] Pico de {rawValue:F2} ({tipo}) detetado!");
                    Console.ResetColor();
                    TriggerEmergencyVideo();

                    foreach (var pkt in _bufferRotina) await PublishAsync(pkt);
                    _bufferRotina.Clear();
                    await PublishAsync(packet);

                    emaDict[tipo] = rawValue;
                    emaVarDict[tipo] = 1f;
                    leiturasCountDict[tipo] = 0;
                }
                else
                {
                    float delta = rawValue - emaDict[tipo];
                    emaDict[tipo] = emaDict[tipo] + alpha * delta;
                    emaVarDict[tipo] = alpha * delta * delta + (1.0f - alpha) * emaVarDict[tipo];

                    _bufferRotina.Add(packet);
                    Console.WriteLine($"[DADO NORMAL MANUAL] {tipo}: {rawValue:F2} -> Buffer ({_bufferRotina.Count}/10)");
                    if (_bufferRotina.Count >= 10)
                    {
                        foreach (var pkt in _bufferRotina) await PublishAsync(pkt);
                        _bufferRotina.Clear();
                        Console.WriteLine("[BATCH ENVIADO] Despacho em bloco (10) executado.");
                    }
                }
            }
        }

        private static async Task HeartbeatLoopAsync()
        {
            while (_isRunning)
            {
                await Task.Delay(30_000);
                if (!_isRunning) break;
                try
                {
                    await PublishAsync(new TelemetryPacket
                    {
                        MsgType = MsgType.STATUS,
                        DataType = DataType.Unknown,
                        SensorID = _sensorId,
                        TimeStamp = NowUnix(),
                        Value = 1f
                    });
                }
                catch { }
            }
        }

        private static async Task PublishAsync(TelemetryPacket packet)
        {
            if (_amqpChannel == null) return;
            packet.CalculateAndSetChecksum();
            string key = Topic.ToRoutingKey(_zona, packet.DataType, packet.SensorID);
            var props = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Transient,
                ContentType = "application/octet-stream",
                Priority = (byte)(packet.MsgType == MsgType.ALERT ? 9 : 0)
            };
            await _amqpChannel.BasicPublishAsync(
                exchange: Topic.Exchange,
                routingKey: key,
                mandatory: false,
                basicProperties: props,
                body: packet.ToBytes());
        }

        private static float BaselineFor(DataType tipo, float fallback)
        {
            return tipo switch
            {
                DataType.Temp => 20.0f,
                DataType.Hum => 50.0f,
                DataType.Ruido => 40.0f,
                DataType.Lum => 100.0f,
                DataType.PM25 or DataType.PM10 => 10.0f,
                _ => fallback
            };
        }

        private static float BaseThresholdFor(DataType tipo)
        {
            return tipo switch
            {
                DataType.Temp => 40.0f,
                DataType.Hum => 50.0f,
                DataType.Lum => 500.0f,
                DataType.Ruido => 80.0f,
                DataType.PM25 or DataType.PM10 => 50.0f,
                _ => 5.0f
            };
        }

        private static uint NowUnix() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static void TriggerEmergencyVideo()
        {
            if (!_isStreamingVideo)
            {
                _isStreamingVideo = true;
                _ = Task.Run(StreamVideoUdpAsync);
            }
        }

        private static void BackgroundVideoCaptureTask()
        {
            uint sequenceNum = 1;
            while (_isRunning)
            {
                var h = new VideoPacketHeader
                {
                    SensorID = _sensorId,
                    TimeStamp = NowUnix(),
                    SequenceNum = sequenceNum,
                    DataSize = 256
                };
                byte[] packet = new byte[16 + 256];
                Buffer.BlockCopy(h.ToBytes(), 0, packet, 0, 16);
                for (int i = 16; i < packet.Length; i++) packet[i] = (byte)_rng.Next(30, 100);

                _videoBuffer.Enqueue(packet);
                if (_videoBuffer.Count > MAX_FRAMES_IN_BUFFER)
                    _videoBuffer.TryDequeue(out _);

                sequenceNum++;
                Thread.Sleep(100);
            }
        }

        private static async Task StreamVideoUdpAsync()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[UDP] A transmitir Frames de Vídeo da RAM (Porta {_gatewayUdpPort})...");
            Console.ResetColor();
            using var udpClient = new UdpClient();

            var dump = _videoBuffer.ToArray();
            foreach (var frame in dump)
                await udpClient.SendAsync(frame, frame.Length, GATEWAY_IP, _gatewayUdpPort);
            Console.WriteLine($"[UDP] Dump da RAM Concluído ({dump.Length} frames pré-anomalia)");

            uint lastSeq = dump.Length > 0 ? VideoPacketHeader.FromBytes(dump[^1]).SequenceNum : 0;
            var end = DateTime.Now.AddSeconds(120);

            while (_isRunning && DateTime.Now < end)
            {
                lastSeq++;
                var h = new VideoPacketHeader
                {
                    SensorID = _sensorId,
                    TimeStamp = NowUnix(),
                    SequenceNum = lastSeq,
                    DataSize = 256
                };
                byte[] packet = new byte[16 + 256];
                Buffer.BlockCopy(h.ToBytes(), 0, packet, 0, 16);
                for (int i = 16; i < packet.Length; i++) packet[i] = (byte)_rng.Next(150, 256);

                await udpClient.SendAsync(packet, packet.Length, GATEWAY_IP, _gatewayUdpPort);
                if (lastSeq % 20 == 0) Console.WriteLine($"[UDP] Frame pós-evento {lastSeq} enviado >>");

                await Task.Delay(100);
            }
            _isStreamingVideo = false;
        }
    }
}
