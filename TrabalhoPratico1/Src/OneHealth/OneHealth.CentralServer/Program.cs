using System.Net;
using System.Net.Sockets;
using OneHealth.Shared; 

// Define a escuta na porta 6000 para todos os IPs locais
TcpListener server = new TcpListener(IPAddress.Any, 6000);
server.Start();
Console.WriteLine("Servidor Central: A aguardar conexões na porta 6000...");

while (true)
{
    // Aceita múltiplos Gateways (concorrência)
    TcpClient client = server.AcceptTcpClient();
    Console.WriteLine($"Servidor Central: Gateway conectado do IP {client.Client.RemoteEndPoint}");
    
    _ = Task.Run(() => {
        try {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[16];
                while (true)
                {
                    // Tenta ler exatamente 16 bytes
                    int bytesRead = 0;
                    while (bytesRead < 16) {
                        int n = stream.Read(buffer, bytesRead, 16 - bytesRead);
                        if (n <= 0) {
                            Console.WriteLine($"[LOG SERVER] Gateway {client.Client.RemoteEndPoint} desconectado.");
                            return; 
                        }
                        bytesRead += n;
                    }
                    
                    var p = ProtocolPacket.FromBytes(buffer);
                    Console.WriteLine($"[LOG SERVER] {DateTime.Now:T} - Sensor: S{p.SensorID} | Tipo: {p.DataType} | Valor: {p.Value} | Time: {p.Timestamp}");
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"[LOG SERVER] Erro na conexão: {ex.Message}");
        }
    });
}