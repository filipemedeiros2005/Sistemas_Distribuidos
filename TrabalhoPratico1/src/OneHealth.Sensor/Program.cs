#nullable enable
using System;
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
        
        // Portas dinâmicas para suportar múltiplos Gateways
        private static int _gatewayTcpPort = 5001;
        private static int _gatewayUdpPort = 6000;

        private static bool _isRunning = true;
        private static bool _isStreamingVideo = false;
        private static TcpClient? _tcpClient;
        private static NetworkStream? _stream;

        static async Task Main(string[] args)
        {
            // 1. Processar ID
            if (args.Length > 0 && uint.TryParse(args[0], out uint parsedId)) {
                _sensorId = parsedId;
            }
            
            // 2. Processar Modo
            string forceMode = (args.Length > 1 && args[1].ToLower() == "auto") ? "1" : "";

            // 3. Processar Porta do Gateway (Argumento 3 do Script)
            if (args.Length > 2 && int.TryParse(args[2], out int gwPort)) {
                _gatewayTcpPort = gwPort;
                _gatewayUdpPort = gwPort + 999;
            }

            Console.WriteLine($"=== [SENSOR {_sensorId}] -> A ligar ao Gateway em TCP:{_gatewayTcpPort} / UDP:{_gatewayUdpPort} ===");
            
            Console.CancelKeyPress += (sender, e) => {
                e.Cancel = true;
                _isRunning = false;
            };

            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(GATEWAY_IP, _gatewayTcpPort); // Ligação dinâmica
                _stream = _tcpClient.GetStream();

                var heloPacket = new TelemetryPacket { MsgType = MsgType.HELO, SensorID = _sensorId };
                await SendPacketAsync(heloPacket);
                Console.WriteLine($"[SENSOR {_sensorId}] HELO enviado. Ligação autorizada.");

                string opcao = forceMode;
                if (string.IsNullOrEmpty(opcao))
                {
                    Console.WriteLine("\nSelecione o modo de operação:");
                    Console.WriteLine("1. Modo Automático (Algoritmo 3-Sigma via CSV)");
                    Console.WriteLine("2. Modo Manual (Inserir dados via teclado)");
                    Console.Write("Opção: ");
                    opcao = Console.ReadLine() ?? "1";
                    Console.WriteLine();
                }

                if (opcao == "1") await RunAutoSimulationAsync();
                else await RunManualModeAsync();
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

        // --- MODO AUTOMÁTICO (O Algoritmo 3-Sigma analisa os dados do CSV) ---
        private static async Task RunAutoSimulationAsync()
        {
            string csvPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "simulation", $"sensor_{_sensorId}.csv"));

            if (File.Exists(csvPath))
            {
                Console.WriteLine($"[INFO] CSV detetado ({csvPath}). A iniciar Algoritmo 3-Sigma...");
                var linhas = await File.ReadAllLinesAsync(csvPath);
                
                // Variáveis do Algoritmo Estatístico
                float alpha = 0.2f; // Peso do dado atual
                float ema = 0f;     // Média Móvel Exponencial
                float emaVar = 1f;  // Variância Móvel Exponencial (Começa em 1 para evitar divisão por zero inicial)
                bool primeiraLeitura = true;
                int leiturasCount = 0;

                foreach (var linha in linhas)
                {
                    if (!_isRunning) break;
                    if (string.IsNullOrWhiteSpace(linha) || linha.StartsWith("Tipo")) continue;

                    var partes = linha.Split(',');
                    if (partes.Length >= 2 && Enum.TryParse(partes[0], out DataType tipo) && float.TryParse(partes[1], out float rawValue))
                    {
                        if (primeiraLeitura) { ema = rawValue; primeiraLeitura = false; }

                        leiturasCount++;

                        // O VERDADEIRO ALGORITMO 3-SIGMA
                        float desvioPadrao = (float)Math.Sqrt(emaVar);
                        
                        // Limitador base: O desvio padrão não deve ser minúsculo, para evitar que um aumento de 0.1 dispare alertas
                        float threshold = Math.Max(3 * desvioPadrao, 5.0f); 
                        float diferenca = Math.Abs(rawValue - ema);

                        // Só começamos a detetar anomalias após 3 leituras para o modelo estabilizar
                        bool isAlerta = (diferenca > threshold) && (leiturasCount > 3); 

                        if (isAlerta) 
                        {
                            TriggerEmergencyVideo();
                            // Em caso de anomalia, NÃO atualizamos a média nem a variância, para não "envenenar" o nosso modelo de normalidade.
                        }
                        else
                        {
                            // Se for um dado normal, atualiza a Média (EMA) e a Variância (EMV)
                            float diff = rawValue - ema;
                            ema = ema + alpha * diff;
                            emaVar = (1.0f - alpha) * (emaVar + alpha * diff * diff);
                        }

                        var packet = new TelemetryPacket
                        {
                            MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA,
                            DataType = tipo,
                            SensorID = _sensorId,
                            TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            Value = rawValue
                        };

                        await SendPacketAsync(packet);
                        Console.WriteLine($"[3-SIGMA] {packet.MsgType} | {tipo}: {rawValue:F2} (Média: {ema:F2} | Limite: ±{threshold:F2})");
                    }
                    await Task.Delay(5000); 
                }
                Console.WriteLine("[INFO] Ficheiro CSV concluído. A manter ligação ativa.");
                while (_isRunning) await Task.Delay(1000);
            }
            else
            {
                Console.WriteLine($"[AVISO] CSV não encontrado ({csvPath}). A iniciar Simulação Aleatória...");
                await RunEmaSimulationAsync();
            }
        }

        // --- MODO EMA ALEATÓRIO (Fallback) ---
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
                    TriggerEmergencyVideo();
                }

                ema = (rawValue * alpha) + (ema * (1.0f - alpha));
                var packet = new TelemetryPacket
                {
                    MsgType = isDisaster ? MsgType.ALERT : MsgType.DATA,
                    DataType = DataType.Temp,
                    SensorID = _sensorId,
                    TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Value = ema
                };

                await SendPacketAsync(packet);
                Console.WriteLine($"[AUTO-EMA] {packet.MsgType} | Temp: {ema:F2}ºC");
                await Task.Delay(5000);
            }
        }

        // --- MODO MANUAL ---
        private static async Task RunManualModeAsync()
        {
            Console.WriteLine("\n[MODO MANUAL] Tipos Suportados: 1:PM10, 2:PM25, 3:Temp, 4:Hum, 5:Ruido, 6:Lum");
            while (_isRunning)
            {
                Console.Write("\nEscolha o Tipo (1-6) ou 'Q' para sair: ");
                string? tipoInput = Console.ReadLine();
                if (tipoInput?.ToUpper() == "Q") break;

                if (!byte.TryParse(tipoInput, out byte tipoByte) || tipoByte < 1 || tipoByte > 6) continue;
                DataType tipoEscolhido = (DataType)tipoByte;

                Console.Write($"Digite o valor para {tipoEscolhido}: ");
                if (float.TryParse(Console.ReadLine(), out float valorManual))
                {
                    bool isAlerta = (tipoEscolhido == DataType.Temp && valorManual > 35.0f) || (tipoEscolhido == DataType.Ruido && valorManual > 80.0f); 
                    if (isAlerta) TriggerEmergencyVideo();

                    var packet = new TelemetryPacket
                    {
                        MsgType = isAlerta ? MsgType.ALERT : MsgType.DATA, DataType = tipoEscolhido,
                        SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Value = valorManual
                    };

                    await SendPacketAsync(packet);
                    Console.WriteLine($"[ENVIADO] {packet.MsgType} | {tipoEscolhido}: {valorManual}");
                }
            }
        }

        // --- GESTÃO DE VÍDEO ---
        private static void TriggerEmergencyVideo()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("⚠️ [SENSOR] ANOMALIA DETETADA PELO ALGORITMO!");
            Console.ResetColor();

            if (!_isStreamingVideo)
            {
                _isStreamingVideo = true;
                _ = Task.Run(() => StreamVideoUdpAsync());
            }
        }

        private static async Task StreamVideoUdpAsync()
        {
            Console.WriteLine("[VÍDEO] A iniciar transmissão UDP paralela...");
            using var udpClient = new UdpClient();
            uint seqNum = 1;
            var endTime = DateTime.Now.AddSeconds(10); // Transmite 10s

            while (_isRunning && DateTime.Now < endTime)
            {
                var videoHeader = new VideoPacketHeader { SensorID = _sensorId, TimeStamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), SequenceNum = seqNum++, DataSize = 256 };
                byte[] headerBytes = videoHeader.ToBytes();
                byte[] fakePayload = new byte[256]; 
                new Random().NextBytes(fakePayload); 
                
                byte[] fullPacket = new byte[headerBytes.Length + fakePayload.Length];
                Buffer.BlockCopy(headerBytes, 0, fullPacket, 0, headerBytes.Length);
                Buffer.BlockCopy(fakePayload, 0, fullPacket, headerBytes.Length, fakePayload.Length);

                // Envia para a porta UDP do Gateway correspondente
                await udpClient.SendAsync(fullPacket, fullPacket.Length, GATEWAY_IP, _gatewayUdpPort);
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
                var byePacket = new TelemetryPacket { MsgType = MsgType.BYE, SensorID = _sensorId };
                await SendPacketAsync(byePacket);
                _stream?.Close(); _tcpClient?.Close();
            }
        }
    }
}