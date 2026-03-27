using System.Runtime.InteropServices;
namespace OneHealth.Shared;

[StructLayout(LayoutKind.Sequential, Pack = 1)] // Evita dados corrompidos devido a alinhamento de memória
public class ProtocolPacket
{
    public byte MsgType; //1 byte (offset 0)
    // 1. HELO, 2. DATA, 3. ACK, 4. BYE
    public byte DataType; //1 byte (offset 1)
    // 1. PM10, 2. PPM, 3. Temp, 4. Hum
    

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2 /*array tem de estar dentro da estrutura e paddding de 2 bytes*/)] 
    // Converte C# num array de bytes
    private byte[] _reserved = new Byte[2]; // 2 bytes (padding) (offset 2)

    public uint SensorID; // 4 bytes (offset 4) 
    public float Value; // 4 bytes - IEEE 754 (offset 8)
    public uint Timestamp; // 4 bytes
    
    // Converter a estrutura em bytes para enviar via socket
    public byte[] ToBytes()
    {
        int size = Marshal.SizeOf(this);
        byte[] arr = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        
        // Copia a estrutura para a memória alocada (Faltava este passo!)
        Marshal.StructureToPtr(this, ptr, false);
        
        Marshal.Copy(ptr, arr, 0, size);
        Marshal.FreeHGlobal(ptr);
        return arr;
    }

    // Converter os bytes recebidos via socket para a estrutura
    public static ProtocolPacket FromBytes(byte[] arr)
    {
        ProtocolPacket packet = new ProtocolPacket(); // Cria uma nova instância da estrutura
        int size = Marshal.SizeOf(packet); // Mede os 16 bytes
        IntPtr ptr = Marshal.AllocHGlobal(size); // Aloca a memória no sistema
        Marshal.Copy(arr, 0, ptr, size); // Copia os bytes do array para a memória alocada
        
        packet = (ProtocolPacket)Marshal.PtrToStructure(ptr, typeof(ProtocolPacket))!; // Converte a memória alocada para a estrutura
        
        Marshal.FreeHGlobal(ptr); // Liberta a memória para evitar Memory Leaks
        return packet; // Retorna a estrutura preenchida com os dados dos bytes
    }
}
