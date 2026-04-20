#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using OneHealth.Common;

namespace OneHealth.Sensor
{
    class Program
    {
        private static uint _sensorId = 101; 
        private const string GATEWAY_IP = "127.0.0.1";
        private static int _gatewayTcpPort = 5001;
        private static int _gatewayUdpPort = 6000;

        private static bool _isRunning = true;
        private static bool _isStreamingVideo = false;
        private static TcpClient? _tcpClient;
        private static NetworkStream? _stream;
        private static readonly List<TelemetryPacket> _bufferRotina = new();
        private static readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _videoBuffer = new();
        private static readonly int MAX_FRAMES_IN_BUFFER = 300; // 30s @ 10fps

        static async Task Main(string[] args)
        {
            _ = Task.Run(() => BackgroundVideoCaptureTask());
            if (args.Length > 0 && uint.TryParse(args[0], out uint parsedId)) _sensorId = parsedId;
            
            string modoOperacao = args.Length > 1 ? args[1].ToLower() : "auto";
            
            if (args.Length > 2 && int.TryParse(args[2], out int gwPort)) { _gatewayTcpPort = gwPort; _gatewayUdpPort = gwPort + 999; }

            Console.WriteLine($"=== [SENSOR {_sensorId}] a ligar a {_gatewayTcpPort} ===");
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; _isRunning = false; };

            try {
                _tcpClient = new TcpClient(); 
                await _tcpClient.ConnectAsync(GATEWAY_IP, _gatewayTcpPort); 
                _stream = _tcpClient.GetStream();
                await SendPacketAsync(new TelemetryPacket { MsgType = MsgType.HELO, SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

                if (modoOperacao == "manual") await RunManualModeAsync();
                else await RunAutoSimulationAsync(); 
            }
            catch (Exception ex) { Console.WriteLine($"[ERRO DE LIGACAO] {ex.Message}"); }
            finally { if (_tcpClient != null && _tcpClient.Connected) await SendPacketAsync(new TelemetryPacket { MsgType = MsgType.BYE, SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() }); }
        }

        private static async Task RunAutoSimulationAsync()
        {
            string baseDir = AppContext.BaseDirectory;
            string pathRaiz = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "data", "simulation", $"sensor_{_sensorId}.csv"));
            string pathSrc = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "data", "simulation", $"sensor_{_sensorId}.csv"));
            string csvPath = File.Exists(pathRaiz) ? pathRaiz : (File.Exists(pathSrc) ? pathSrc : "");

            if (string.IsNullOrEmpty(csvPath)) {
                Console.WriteLine($"[ERRO FATAL] CSV nao encontrado em lado nenhum. A parar."); return;
            }

            Console.WriteLine($"[INFO] Algoritmo 3-Sigma Iniciado.");
            var linhas = await File.ReadAllLinesAsync(csvPath);

            while (_isRunning) 
            {
                var emaDict = new Dictionary<DataType, float>();
                var emaVarDict = new Dictionary<DataType, float>();
                var leiturasCountDict = new Dictionary<DataType, int>();
                float alpha = 0.2f;

                foreach (var linha in linhas)
                {
                    if (!_isRunning) break;
                    if (string.IsNullOrWhiteSpace(linha) || !linha.Contains(",")) continue;
                    var p = linha.Split(',');
                    string valorFormatado = p[1].Replace(',', '.');

                    if (Enum.TryParse(p[0], out DataType tipo) && float.TryParse(valorFormatado, NumberStyles.Any, CultureInfo.InvariantCulture, out float rawValue))
                    {
                        if (!emaDict.ContainsKey(tipo)) {
                            switch (tipo) {
                                case DataType.Temp: emaDict[tipo] = 20.0f; break;
                                case DataType.Hum: emaDict[tipo] = 50.0f; break;
                                case DataType.Ruido: emaDict[tipo] = 40.0f; break;
                                case DataType.Lum: emaDict[tipo] = 100.0f; break;
                                case DataType.PM25: case DataType.PM10: emaDict[tipo] = 10.0f; break;
                                default: emaDict[tipo] = rawValue; break;
                            }
                            emaVarDict[tipo] = 1f;
                            leiturasCountDict[tipo] = 2; // Forçar que a primeira leitura possa despontar alertas instantaneamente
                        }
                        leiturasCountDict[tipo]++;

                        float baseThreshold = 5.0f;
                        if (tipo == DataType.Temp) baseThreshold = 15.0f; // Aceita 5ºC a 35ºC no primeiro valor
                        if (tipo == DataType.Hum) baseThreshold = 25.0f;  // Aceita 25% a 75% no primeiro valor
                        if (tipo == DataType.Lum) baseThreshold = 200.0f;
                        if (tipo == DataType.Ruido) baseThreshold = 25.0f;
                        if (tipo == DataType.PM25 || tipo == DataType.PM10) baseThreshold = 20.0f;

                        float desvioPadrao = (float)Math.Sqrt(emaVarDict[tipo]);
                        float threshold = Math.Max(3 * desvioPadrao, baseThreshold); 
                        bool isAlerta = (Math.Abs(rawValue - emaDict[tipo]) > threshold) && (leiturasCountDict[tipo] > 2); 

                        var packet = new TelemetryPacket {
                            MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA, DataType = tipo, SensorID = _sensorId,
                            TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Value = rawValue
                        };

                        if (isAlerta) {
                            Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ALERTA 3-SIGMA] Pico de {rawValue:F2} ({tipo}) detetado!"); Console.ResetColor();
                            TriggerEmergencyVideo();
                            foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                            _bufferRotina.Clear();
                            await SendPacketAsync(packet);

                            emaDict[tipo] = rawValue;
                            emaVarDict[tipo] = 1f;
                            leiturasCountDict[tipo] = 0;
                        } else {
                            emaDict[tipo] = emaDict[tipo] + alpha * (rawValue - emaDict[tipo]);
                            emaVarDict[tipo] = (1.0f - alpha) * (emaVarDict[tipo] + alpha * ((rawValue - emaDict[tipo]) * (rawValue - emaDict[tipo])));
                            
                            _bufferRotina.Add(packet);
                            Console.WriteLine($"[DADO NORMAL] {tipo}: {rawValue:F2} -> Buffer ({_bufferRotina.Count}/10)");
                            if (_bufferRotina.Count >= 10) {
                                foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                                _bufferRotina.Clear();
                                Console.WriteLine("[BATCH ENVIADO] Lote de 10 pacotes despachado (Otimização Energética).");
                            }
                        }
                    }
                    await Task.Delay(5000); 
                }
            }
        }

        private static async Task RunManualModeAsync() {
            Console.WriteLine("\n=== [ MODO MANUAL INICIADO ] ===");
            Console.WriteLine("Insira os dados separando o tipo do valor com um espaco.");
            Console.WriteLine("Exemplo: Temp 35.5");
            Console.WriteLine("Tipos permitidos: Temp, Hum, Ruido, PM10, PM25, Lum");

            var emaDict = new Dictionary<DataType, float>();
            var emaVarDict = new Dictionary<DataType, float>();
            var leiturasCountDict = new Dictionary<DataType, int>();
            float alpha = 0.2f;

            while (_isRunning) {
                Console.Write("\n> ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;
                
                var parts = input.Split(' ');
                if (parts.Length == 2 && Enum.TryParse(parts[0], true, out DataType tipo)) {
                    string valFormatado = parts[1].Replace(',', '.');
                    if (float.TryParse(valFormatado, NumberStyles.Any, CultureInfo.InvariantCulture, out float rawValue)) {
                        
                        if (!emaDict.ContainsKey(tipo)) {
                            switch (tipo) {
                                case DataType.Temp: emaDict[tipo] = 20.0f; break;
                                case DataType.Hum: emaDict[tipo] = 50.0f; break;
                                case DataType.Ruido: emaDict[tipo] = 40.0f; break;
                                case DataType.Lum: emaDict[tipo] = 100.0f; break;
                                case DataType.PM25: case DataType.PM10: emaDict[tipo] = 10.0f; break;
                                default: emaDict[tipo] = rawValue; break;
                            }
                            emaVarDict[tipo] = 1f;
                            leiturasCountDict[tipo] = 2; // Para autorizar despoletar alerta log na 1ª leitura manual
                        }
                        leiturasCountDict[tipo]++;

                        float baseThreshold = 5.0f;
                        if (tipo == DataType.Temp) baseThreshold = 15.0f;
                        if (tipo == DataType.Hum) baseThreshold = 25.0f;
                        if (tipo == DataType.Lum) baseThreshold = 200.0f;
                        if (tipo == DataType.Ruido) baseThreshold = 25.0f;
                        if (tipo == DataType.PM25 || tipo == DataType.PM10) baseThreshold = 20.0f;

                        float desvioPadrao = (float)Math.Sqrt(emaVarDict[tipo]);
                        float threshold = Math.Max(3 * desvioPadrao, baseThreshold); 
                        bool isAlerta = (Math.Abs(rawValue - emaDict[tipo]) > threshold) && (leiturasCountDict[tipo] > 2); 

                        var packet = new TelemetryPacket {
                            MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA, DataType = tipo, SensorID = _sensorId,
                            TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Value = rawValue
                        };
                        
                        if (isAlerta) {
                            Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ALERTA 3-SIGMA MANUAL] Pico de {rawValue:F2} ({tipo}) detetado!"); Console.ResetColor();
                            TriggerEmergencyVideo();
                            foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                            _bufferRotina.Clear();
                            await SendPacketAsync(packet);

                            emaDict[tipo] = rawValue;
                            emaVarDict[tipo] = 1f;
                            leiturasCountDict[tipo] = 0;
                        } else {
                            emaDict[tipo] = emaDict[tipo] + alpha * (rawValue - emaDict[tipo]);
                            emaVarDict[tipo] = (1.0f - alpha) * (emaVarDict[tipo] + alpha * ((rawValue - emaDict[tipo]) * (rawValue - emaDict[tipo])));
                            
                            _bufferRotina.Add(packet);
                            Console.WriteLine($"[DADO NORMAL MANUAL] {tipo}: {rawValue:F2} -> Buffer ({_bufferRotina.Count}/10)");
                            if (_bufferRotina.Count >= 10) {
                                foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                                _bufferRotina.Clear();
                                Console.WriteLine("[BATCh ENVIADO] Despacho em bloco (10) executado igual ao Automático.");
                            }
                        }
                    } else {
                        Console.WriteLine("[ERRO] O valor inserido nao e um numero valido.");
                    }
                } else {
                    Console.WriteLine("[ERRO] Formato invalido. Use 'Tipo Valor'.");
                }
            }
        }

        private static void TriggerEmergencyVideo() {
            if (!_isStreamingVideo) { _isStreamingVideo = true; _ = Task.Run(() => StreamVideoUdpAsync()); }
        }

        private static void BackgroundVideoCaptureTask()
        {
            uint sequenceNum = 1;
            while (_isRunning)
            {
                var h = new VideoPacketHeader { SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), SequenceNum = sequenceNum, DataSize = 256 };
                byte[] packet = new byte[16 + 256];
                Buffer.BlockCopy(h.ToBytes(), 0, packet, 0, 16);
                
                var rnd = new Random();
                // Simular um padrão de vídeo com ruído denso e normal (TV estática escura)
                for(int i=16; i<packet.Length; i++) packet[i] = (byte)rnd.Next(30, 100);

                _videoBuffer.Enqueue(packet);
                if (_videoBuffer.Count > MAX_FRAMES_IN_BUFFER) {
                    _videoBuffer.TryDequeue(out _); // FIFO rotativo (30 segundos na RAM)
                }

                sequenceNum++;
                System.Threading.Thread.Sleep(100); // 10 FPS
            }
        }

        private static async Task StreamVideoUdpAsync() {
            Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine($"[UDP] A transmitir Frames de Video da RAM (Porta {_gatewayUdpPort})..."); Console.ResetColor();
            using var udpClient = new UdpClient(); 
            
            // 1. DUMP DA RAM (30s)
            var dump = _videoBuffer.ToArray();
            foreach(var frame in dump) {
                await udpClient.SendAsync(frame, frame.Length, GATEWAY_IP, _gatewayUdpPort);
            }
            Console.WriteLine($"[UDP] Dump da RAM Concluído ({dump.Length} frames pre-anomalia)");

            // 2. Transmissão pós-anomalia contínua ao vivo (120s)
            var end = DateTime.Now.AddSeconds(120);
            uint lastSeq = dump.Length > 0 ? VideoPacketHeader.FromBytes(dump[^1]).SequenceNum : 0;
            
            while (_isRunning && DateTime.Now < end) {
                lastSeq++;
                var h = new VideoPacketHeader { SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), SequenceNum = lastSeq, DataSize = 256 };
                byte[] packet = new byte[16 + 256];
                Buffer.BlockCopy(h.ToBytes(), 0, packet, 0, 16);
                
                var rnd = new Random();
                // Ruído agressivo brilhante pós anomalia (CCTV comprometida)
                for(int i=16; i<packet.Length; i++) packet[i] = (byte)rnd.Next(150, 256);

                await udpClient.SendAsync(packet, packet.Length, GATEWAY_IP, _gatewayUdpPort);
                if (lastSeq % 20 == 0) Console.WriteLine($"[UDP] Frame pós-evento {lastSeq} enviado >>");
                
                await Task.Delay(100);
            }
            _isStreamingVideo = false;
        }

        private static async Task SendPacketAsync(TelemetryPacket packet) {
            packet.CalculateAndSetChecksum();
            if (_stream != null) await _stream.WriteAsync(packet.ToBytes());
        }
    }
}