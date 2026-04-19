#nullable enable
using System;
using System.Collections.Generic;
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
            string forceMode = (args.Length > 1 && args[1].ToLower() == "auto") ? "1" : "";
            if (args.Length > 2 && int.TryParse(args[2], out int gwPort)) { _gatewayTcpPort = gwPort; _gatewayUdpPort = gwPort + 999; }

            Console.WriteLine($"=== [SENSOR {_sensorId}] a ligar a {_gatewayTcpPort} ===");
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; _isRunning = false; };

            try
            {
                _tcpClient = new TcpClient(); await _tcpClient.ConnectAsync(GATEWAY_IP, _gatewayTcpPort); _stream = _tcpClient.GetStream();
                // AQUI FALTAVA O TIMESTAMP! O Servidor precisa dele para a DB.
                await SendPacketAsync(new TelemetryPacket { MsgType = MsgType.HELO, SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

                string opcao = forceMode == "" ? "1" : forceMode;
                if (opcao == "1") await RunAutoSimulationAsync();
                else await RunManualModeAsync(); 
            }
            catch (Exception ex) { Console.WriteLine($"[ERRO] {ex.Message}"); }
            finally { if (_tcpClient != null && _tcpClient.Connected) await SendPacketAsync(new TelemetryPacket { MsgType = MsgType.BYE, SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() }); }
        }

        private static async Task RunAutoSimulationAsync()
        {
            string csvPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "simulation", $"sensor_{_sensorId}.csv"));
            if (!File.Exists(csvPath)) return;

            var linhas = await File.ReadAllLinesAsync(csvPath);
            float alpha = 0.2f, ema = 0f, emaVar = 1f;
            bool primeiraLeitura = true; int leiturasCount = 0;

            foreach (var linha in linhas)
            {
                if (!_isRunning) break;
                if (string.IsNullOrWhiteSpace(linha) || !linha.Contains(",")) continue;
                var p = linha.Split(',');

                if (Enum.TryParse(p[0], out DataType tipo) && float.TryParse(p[1], out float rawValue))
                {
                    if (primeiraLeitura) { ema = rawValue; primeiraLeitura = false; }
                    leiturasCount++;

                    float threshold = Math.Max(3 * (float)Math.Sqrt(emaVar), 5.0f); 
                    bool isAlerta = (Math.Abs(rawValue - ema) > threshold) && (leiturasCount > 3); 

                    var packet = new TelemetryPacket {
                        MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA, DataType = tipo, SensorID = _sensorId,
                        TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Value = rawValue
                    };

                    if (isAlerta) 
                    {
                        Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ALERTA 3-SIGMA] {rawValue:F2}"); Console.ResetColor();
                        TriggerEmergencyVideo();
                        
                        foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                        _bufferRotina.Clear();
                        await SendPacketAsync(packet);
                    }
                    else 
                    {
                        ema = ema + alpha * (rawValue - ema);
                        emaVar = (1.0f - alpha) * (emaVar + alpha * (rawValue - ema) * (rawValue - ema));
                        
                        _bufferRotina.Add(packet);
                        Console.WriteLine($"[DADO GERADO] Agendado no Buffer (Tamanho atual: {_bufferRotina.Count}/2)");
                        
                        // LOTE DE 2 para feedback rápido na apresentação (10s de espera)
                        if (_bufferRotina.Count >= 2) {
                            foreach(var pkt in _bufferRotina) await SendPacketAsync(pkt);
                            _bufferRotina.Clear();
                            Console.WriteLine("[BATCH ENVIADO] Lote despachado.");
                        }
                    }
                }
                await Task.Delay(5000); 
            }
            while (_isRunning) await Task.Delay(1000);
        }

        private static void TriggerEmergencyVideo() {
            if (!_isStreamingVideo) { _isStreamingVideo = true; _ = Task.Run(() => StreamVideoUdpAsync()); }
        }

        private static async Task StreamVideoUdpAsync() {
            using var udpClient = new UdpClient(); uint seq = 1; var end = DateTime.Now.AddSeconds(10);
            while (_isRunning && DateTime.Now < end) {
                var h = new VideoPacketHeader { SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), SequenceNum = seq++, DataSize = 256 };
                byte[] b = new byte[16 + 256]; Buffer.BlockCopy(h.ToBytes(), 0, b, 0, 16); new Random().NextBytes(b.AsSpan(16).ToArray());
                await udpClient.SendAsync(b, b.Length, GATEWAY_IP, _gatewayUdpPort);
                await Task.Delay(50);
            }
            _isStreamingVideo = false;
        }

        private static async Task SendPacketAsync(TelemetryPacket packet) {
            packet.CalculateAndSetChecksum();
            if (_stream != null) await _stream.WriteAsync(packet.ToBytes());
        }
        private static async Task RunManualModeAsync() { while(true) await Task.Delay(1000); }
    }
}