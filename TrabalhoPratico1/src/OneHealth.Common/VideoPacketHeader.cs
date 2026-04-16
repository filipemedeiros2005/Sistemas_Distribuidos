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
            byte[] arr = new byte[16];
            Span<byte> span = arr;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), SensorID);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), TimeStamp);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), SequenceNum);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12, 4), DataSize);
            return arr;
        }

        public static VideoPacketHeader FromBytes(byte[] arr)
        {
            if (arr.Length < 16) throw new ArgumentException("Array too short");
            Span<byte> span = arr;
            return new VideoPacketHeader
            {
                SensorID = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span.Slice(0, 4)),
                TimeStamp = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4)),
                SequenceNum = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4)),
                DataSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span.Slice(12, 4))
            };
        }
    }
}
