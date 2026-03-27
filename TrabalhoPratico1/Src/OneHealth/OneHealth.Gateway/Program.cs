using System.Net;
using System.Net.Sockets;
using OneHealth.Shared;

// O .csproj vai copiar o CSV para aqui automaticamente se configurado corretamente
string csvPath = Path.Combine(AppContext.BaseDirectory, "sensors_config.csv");

if (!File.Exists(csvPath))
{
    Console.WriteLine($"ERRO: Ficheiro {csvPath} não encontrado!");
    return;
}

var validSensors = File.ReadAllLines(csvPath)
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .Select(line => line.Split(':')[0].Trim())
    .ToHashSet();

TcpListener listener = new TcpListener(IPAddress.Any, 5001);
listener.Start();
Console.WriteLine("Gateway: A aguardar sensores na porta 5001...");

// O Gateway tenta ligar-se ao Servidor Central (Porta 6000)
TcpClient serverConnection = null!;
while (true) {
    try {
        serverConnection = new TcpClient("127.0.0.1", 6000);
        Console.WriteLine("Gateway: Ligado ao Servidor Central.");
        break;
    } catch {
        Console.WriteLine("Gateway: Servidor Central não encontrado. A tentar novamente em 2s...");
        Thread.Sleep(2000);
    }
}
using NetworkStream serverStream = serverConnection.GetStream();
object serverLock = new object();

while (true)
{
    TcpClient sensorClient = listener.AcceptTcpClient(); 
    Console.WriteLine($"Gateway: Sensor conectado do IP {sensorClient.Client.RemoteEndPoint}");
    
    _ = Task.Run(() => {
        try {
            using (sensorClient)
            using (NetworkStream sensorStream = sensorClient.GetStream())
            {
                byte[] buffer = new byte[16];
                Console.WriteLine($"[DEBUG] Gateway: A aguardar dados de {sensorClient.Client.RemoteEndPoint}...");
                while (true) 
                {
                    // Tenta ler exatamente 16 bytes
                    int bytesRead = 0;
                    while (bytesRead < 16) {
                        int n = sensorStream.Read(buffer, bytesRead, 16 - bytesRead);
                        if (n <= 0) {
                            Console.WriteLine($"Gateway: Sensor de {sensorClient.Client.RemoteEndPoint} desconectado.");
                            return; 
                        }
                        bytesRead += n;
                    }
                    Console.WriteLine($"[DEBUG] Gateway: Recebeu {bytesRead} bytes de S{sensorClient.Client.RemoteEndPoint}.");

                    var packet = ProtocolPacket.FromBytes(buffer); 
                    if (validSensors.Contains("S" + packet.SensorID))
                    {
                        Console.WriteLine($"[DEBUG] Sensor S{packet.SensorID} validado. A encaminhar...");
                        lock (serverLock) {
                            serverStream.Write(buffer, 0, 16); 
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] Sensor S{packet.SensorID} inválido. A descartar...");
                    }
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Gateway: Erro a processar sensor: {ex.Message}");
        }
    });
}