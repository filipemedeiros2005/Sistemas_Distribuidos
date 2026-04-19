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

        static async Task Main(string[] args)
        {
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
                float alpha = 0.2f, ema = 0f, emaVar = 1f;
                bool primeiraLeitura = true;
                int leiturasCount = 0;

                foreach (var linha in linhas)
                {
                    if (!_isRunning) break;
                    if (string.IsNullOrWhiteSpace(linha) || !linha.Contains(",")) continue;
                    var p = linha.Split(',');
                    string valorFormatado = p[1].Replace(',', '.');

                    if (Enum.TryParse(p[0], out DataType tipo) && float.TryParse(valorFormatado, NumberStyles.Any, CultureInfo.InvariantCulture, out float rawValue))
                    {
                        if (primeiraLeitura) { ema = rawValue; primeiraLeitura = false; }
                        leiturasCount++;

                        float baseThreshold = 5.0f;
                        if (tipo == DataType.Lum) baseThreshold = 200.0f;
                        if (tipo == DataType.Ruido) baseThreshold = 25.0f;
                        if (tipo == DataType.PM25 || tipo == DataType.PM10) baseThreshold = 20.0f;

                        float desvioPadrao = (float)Math.Sqrt(emaVar);
                        float threshold = Math.Max(3 * desvioPadrao, baseThreshold); 
                        bool isAlerta = (Math.Abs(rawValue - ema) > threshold) && (leiturasCount > 2); 

                        var packet = new TelemetryPacket {
                            MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA, DataType = tipo, SensorID = _sensorId,
                            TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Value = rawValue
                        };

                        if (isAlerta) {
                            Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ALERTA 3-SIGMA] Pico de {rawValue:F2} detetado!"); Console.ResetColor();
                            TriggerEmergencyVideo();
                            foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                            _bufferRotina.Clear();
                            await SendPacketAsync(packet);

                            ema = rawValue;
                            emaVar = 1f;
                            leiturasCount = 0;
                        } else {
                            ema = ema + alpha * (rawValue - ema);
                            emaVar = (1.0f - alpha) * (emaVar + alpha * (rawValue - ema) * (rawValue - ema));
                            
                            _bufferRotina.Add(packet);
                            Console.WriteLine($"[DADO NORMAL] {tipo}: {rawValue:F2} -> Buffer ({_bufferRotina.Count}/2)");
                            if (_bufferRotina.Count >= 2) {
                                foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                                _bufferRotina.Clear();
                                Console.WriteLine("[BATCH ENVIADO] Lote despachado.");
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

            float alpha = 0.2f, ema = 0f, emaVar = 1f;
            bool primeiraLeitura = true;
            int leiturasCount = 0;

            while (_isRunning) {
                Console.Write("\n> ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;
                
                var parts = input.Split(' ');
                if (parts.Length == 2 && Enum.TryParse(parts[0], true, out DataType tipo)) {
                    string valFormatado = parts[1].Replace(',', '.');
                    if (float.TryParse(valFormatado, NumberStyles.Any, CultureInfo.InvariantCulture, out float rawValue)) {
                        
                        if (primeiraLeitura) { ema = rawValue; primeiraLeitura = false; }
                        leiturasCount++;

                        float baseThreshold = 5.0f;
                        if (tipo == DataType.Lum) baseThreshold = 200.0f;
                        if (tipo == DataType.Ruido) baseThreshold = 25.0f;
                        if (tipo == DataType.PM25 || tipo == DataType.PM10) baseThreshold = 20.0f;

                        float desvioPadrao = (float)Math.Sqrt(emaVar);
                        float threshold = Math.Max(3 * desvioPadrao, baseThreshold); 
                        bool isAlerta = (Math.Abs(rawValue - ema) > threshold) && (leiturasCount > 2); 

                        var packet = new TelemetryPacket {
                            MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA, DataType = tipo, SensorID = _sensorId,
                            TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Value = rawValue
                        };
                        
                        if (isAlerta) {
                            Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ALERTA 3-SIGMA MANUAL] Pico de {rawValue:F2} detetado!"); Console.ResetColor();
                            TriggerEmergencyVideo();
                            foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                            _bufferRotina.Clear();
                            await SendPacketAsync(packet);

                            ema = rawValue;
                            emaVar = 1f;
                            leiturasCount = 0;
                        } else {
                            ema = ema + alpha * (rawValue - ema);
                            emaVar = (1.0f - alpha) * (emaVar + alpha * (rawValue - ema) * (rawValue - ema));
                            
                            _bufferRotina.Add(packet);
                            Console.WriteLine($"[DADO NORMAL MANUAL] {tipo}: {rawValue:F2} -> Buffer ({_bufferRotina.Count}/2)");
                            if (_bufferRotina.Count >= 2) {
                                foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                                _bufferRotina.Clear();
                                Console.WriteLine("[BATCH ENVIADO] Lote manual despachado.");
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

        private static async Task StreamVideoUdpAsync() {
            Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine($"[UDP] A transmitir Frames de Video (Porta {_gatewayUdpPort})..."); Console.ResetColor();
            using var udpClient = new UdpClient(); uint seq = 1; var end = DateTime.Now.AddSeconds(10);
            while (_isRunning && DateTime.Now < end) {
                var h = new VideoPacketHeader { SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), SequenceNum = seq, DataSize = 256 };
                byte[] b = new byte[16 + 256]; Buffer.BlockCopy(h.ToBytes(), 0, b, 0, 16); new Random().NextBytes(b.AsSpan(16).ToArray());
                await udpClient.SendAsync(b, b.Length, GATEWAY_IP, _gatewayUdpPort);
                if (seq % 10 == 0) Console.WriteLine($"[UDP] Frame {seq} enviado >>");
                seq++; await Task.Delay(50);
            }
            _isStreamingVideo = false;
        }

        private static async Task SendPacketAsync(TelemetryPacket packet) {
            packet.CalculateAndSetChecksum();
            if (_stream != null) await _stream.WriteAsync(packet.ToBytes());
        }
    }
}