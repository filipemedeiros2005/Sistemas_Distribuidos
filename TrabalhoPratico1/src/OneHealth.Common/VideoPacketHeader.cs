using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

namespace OneHealth.Common
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VideoPacketHeader
    {
        public uint SensorID;       // Offset 0
        public uint TimeStamp;      // Offset 4
        public uint SequenceNum;    // Offset 8
        public uint DataSize;       // Offset 12

        public byte[] ToBytes()
        {
            int size = Marshal.SizeOf(this);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);

            Marshal.StructureToPtr(this, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);

            return arr;
        }

        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "AOT compilation is not required for this component")]
        public static VideoPacketHeader FromBytes(byte[] arr)
        {
            VideoPacketHeader packet = new VideoPacketHeader();
            int size = Marshal.SizeOf(packet);
            IntPtr ptr = Marshal.AllocHGlobal(size);

            Marshal.Copy(arr, 0, ptr, size);
            packet = Marshal.PtrToStructure<VideoPacketHeader>(ptr);
            Marshal.FreeHGlobal(ptr);

            return packet;
        }
    }
}
