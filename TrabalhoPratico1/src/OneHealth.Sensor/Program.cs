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

        private static bool _isRunning = true;
        private static TcpClient? _tcpClient; // <- Adicionado o '?' aqui
        private static NetworkStream? _stream; // <- Adicionado o '?' aqui

        static async Task Main(string[] args)
        {
            Console.WriteLine($"[SENSOR {SENSOR_ID}] A iniciar Unidade One Health...");

            Console.CancelKeyPress += (sender, e) => {
                e.Cancel = true;
                _isRunning = false; // Trigger para fechar limpo
            };

            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(GATEWAY_IP, GATEWAY_TCP_PORT);
                _stream = _tcpClient.GetStream();

                // FASE A: Handshake
                var heloPacket = new TelemetryPacket { MsgType = MsgType.HELO, SensorID = SENSOR_ID };
                await SendPacketAsync(heloPacket);
                Console.WriteLine("[SENSOR] HELO enviado. A iniciar telemetria...");

                await RunEmaSimulationAsync();
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

        private static async Task RunEmaSimulationAsync()
        {
            float alpha = 0.2f;
            float ema = 20.0f; 
            var rnd = new Random();

            while (_isRunning)
            {
                float rawValue = 20.0f + (float)(rnd.NextDouble() * 2.0 - 1.0);
                
                // Anomalia Estatística
                bool isDisaster = rnd.Next(0, 100) < 5;
                if (isDisaster)
                {
                    rawValue += 15.0f;
                    Console.WriteLine("⚠️ [SENSOR] ANOMALIA DETETADA: Pico de calor extremo!");
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
                Console.WriteLine($"[SENSOR] Enviado: {dataPacket.MsgType} | Temp EMA: {ema:F2}ºC");

                await Task.Delay(5000); // Ritmo IoT
            }
        }

        private static async Task SendPacketAsync(TelemetryPacket packet)
        {
            packet.CalculateAndSetChecksum();
            byte[] bytes = packet.ToBytes();
            await _stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static async Task ShutdownGracefully()
        {
            if (_tcpClient != null && _tcpClient.Connected)
            {
                Console.WriteLine("\n[SENSOR] A enviar sinal de BYE...");
                var byePacket = new TelemetryPacket { MsgType = MsgType.BYE, SensorID = SENSOR_ID };
                await SendPacketAsync(byePacket);
                
                _stream?.Close();
                _tcpClient?.Close();
            }
        }
    }
}