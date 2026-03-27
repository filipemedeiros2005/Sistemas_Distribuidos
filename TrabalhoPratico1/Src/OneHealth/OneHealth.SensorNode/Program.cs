using System.Net.Sockets;
using OneHealth.Shared;

Console.WriteLine("Iniciando Sensor S102...");
try {
    using TcpClient client = new TcpClient("127.0.0.1", 5001);
    using NetworkStream stream = client.GetStream();

    // Envia HELO (Fase 2 - Handshake simplificado)
    var helo = new ProtocolPacket { MsgType = 1, SensorID = 102 };
    stream.Write(helo.ToBytes());

    while (true)
    {
        Console.Write("Introduza um valor de temperatura (ou 'sair'): ");
        string? input = Console.ReadLine();
        if (input == "sair") break;

        if (float.TryParse(input, out float val))
        {
            var data = new ProtocolPacket {
                MsgType = 2,
                DataType = 3, // Temperatura
                SensorID = 102,
                Value = val,
                Timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            stream.Write(data.ToBytes());
            Console.WriteLine("Pacote de 16 bytes enviado!");
        }
    }
} catch (Exception ex) {
    Console.WriteLine($"ERRO no Sensor: {ex.Message}");
}
