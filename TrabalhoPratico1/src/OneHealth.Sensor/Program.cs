#nullable enable
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using OneHealth.Common;

namespace OneHealth.Sensor
{
    class Program
    {
        private const uint SENSOR_ID = 101; 
        private const string GATEWAY_IP = "127.0.0.1";
        private const int GATEWAY_TCP_PORT = 5001;
        private const int GATEWAY_UDP_PORT = 6000;

        private static bool _isRunning = true;
        private static bool _isStreamingVideo = false;
        private static TcpClient? _tcpClient;
        private static NetworkStream? _stream;

        static async Task Main(string[] args)
        {
            Console.WriteLine($"=== [SENSOR {SENSOR_ID}] One Health ===");
            
            Console.CancelKeyPress += (sender, e) => {
                e.Cancel = true;
                _isRunning = false;
            };

            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(GATEWAY_IP, GATEWAY_TCP_PORT);
                _stream = _tcpClient.GetStream();

                var heloPacket = new TelemetryPacket { MsgType = MsgType.HELO, SensorID = SENSOR_ID };
                await SendPacketAsync(heloPacket);
                Console.WriteLine("[SENSOR] HELO enviado ao Gateway. Ligação autorizada.");

                // MENU HÍBRIDO (Requisito Fase 2)
                Console.WriteLine("\nSelecione o modo de operação:");
                Console.WriteLine("1. Modo Automático (Simulação EMA + Alertas)");
                Console.WriteLine("2. Modo Manual (Inserir dados via teclado)");
                Console.Write("Opção: ");
                
                string? opcao = Console.ReadLine();
                Console.WriteLine();

                if (opcao == "1")
                {
                    await RunEmaSimulationAsync();
                }
                else
                {
                    await RunManualModeAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SENSOR ERRO] {ex.Message}");
            }
            finally
            {
                await ShutdownGracefully();
            }
        }

        // --- MODO MANUAL ---
        private static async Task RunManualModeAsync()
        {
            Console.WriteLine("[MODO MANUAL] Digite a temperatura (ex: 22,5) ou 'Q' para sair.");
            while (_isRunning)
            {
                Console.Write("Temperatura: ");
                string? input = Console.ReadLine();

                if (input?.ToUpper() == "Q") break;

                if (float.TryParse(input, out float valorManual))
                {
                    bool isAlerta = valorManual > 35.0f; // Exemplo: Alerta se maior que 35
                    var packet = new TelemetryPacket
                    {
                        MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA,
                        DataType = DataType.Temp,
                        SensorID = SENSOR_ID,
                        TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        Value = valorManual
                    };

                    await SendPacketAsync(packet);
                    Console.WriteLine($"[ENVIADO] {packet.MsgType} | Valor: {valorManual}ºC");

                    // Se for alerta, dispara também o vídeo
                    if (isAlerta && !_isStreamingVideo)
                    {
                        _isStreamingVideo = true;
                        _ = Task.Run(() => StreamVideoUdpAsync());
                    }
                }
                else
                {
                    Console.WriteLine("Valor inválido. Use números (ex: 20,5).");
                }
            }
        }

        // --- MODO AUTOMÁTICO (EMA) ---
        private static async Task RunEmaSimulationAsync()
        {
            float alpha = 0.2f; float ema = 20.0f; var rnd = new Random();
            while (_isRunning)
            {
                float rawValue = 20.0f + (float)(rnd.NextDouble() * 2.0 - 1.0);
                bool isDisaster = rnd.Next(0, 100) < 5;
                if (isDisaster)
                {
                    rawValue += 15.0f;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("⚠️ [SENSOR] ANOMALIA DETETADA!");
                    Console.ResetColor();

                    if (!_isStreamingVideo)
                    {
                        _isStreamingVideo = true;
                        _ = Task.Run(() => StreamVideoUdpAsync());
                    }
                }

                ema = (rawValue * alpha) + (ema * (1.0f - alpha));
                var dataPacket = new TelemetryPacket
                {
                    MsgType = isDisaster ? MsgType.ALERT : MsgType.DATA,
                    DataType = DataType.Temp,
                    SensorID = SENSOR_ID,
                    TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Value = ema
                };

                await SendPacketAsync(dataPacket);
                Console.WriteLine($"[AUTO] {dataPacket.MsgType} | Temp EMA: {ema:F2}ºC");
                await Task.Delay(5000);
            }
        }

        // --- STREAM DE VÍDEO (UDP) ---
        private static async Task StreamVideoUdpAsync()
        {
            Console.WriteLine("[VÍDEO] A iniciar transmissão UDP...");
            using var udpClient = new UdpClient();
            uint seqNum = 1;
            var endTime = DateTime.Now.AddSeconds(10); // Transmite 10s

            while (_isRunning && DateTime.Now < endTime)
            {
                var videoHeader = new VideoPacketHeader { SensorID = SENSOR_ID, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), SequenceNum = seqNum++, DataSize = 256 };
                byte[] headerBytes = videoHeader.ToBytes();
                byte[] fakePayload = new byte[256]; // Dados binários simulados
                new Random().NextBytes(fakePayload); 
                
                byte[] fullPacket = new byte[headerBytes.Length + fakePayload.Length];
                Buffer.BlockCopy(headerBytes, 0, fullPacket, 0, headerBytes.Length);
                Buffer.BlockCopy(fakePayload, 0, fullPacket, headerBytes.Length, fakePayload.Length);

                await udpClient.SendAsync(fullPacket, fullPacket.Length, GATEWAY_IP, GATEWAY_UDP_PORT);
                await Task.Delay(50); // 20 FPS
            }
            Console.WriteLine("[VÍDEO] Transmissão concluída.");
            _isStreamingVideo = false;
        }

        private static async Task SendPacketAsync(TelemetryPacket packet)
        {
            packet.CalculateAndSetChecksum();
            byte[] bytes = packet.ToBytes();
            if (_stream != null) await _stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static async Task ShutdownGracefully()
        {
            if (_tcpClient != null && _tcpClient.Connected)
            {
                var byePacket = new TelemetryPacket { MsgType = MsgType.BYE, SensorID = SENSOR_ID };
                await SendPacketAsync(byePacket);
                _stream?.Close(); _tcpClient?.Close();
            }
        }
    }
}