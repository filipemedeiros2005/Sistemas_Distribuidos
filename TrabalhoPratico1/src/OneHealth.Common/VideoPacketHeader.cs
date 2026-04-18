using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OneHealth.Common
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VideoPacketHeader
    {
        public uint SensorID;       
        public uint TimeStamp;      
        public uint SequenceNum;    
        public uint DataSize;       

        public byte[] ToBytes()
        {
            byte[] arr = new byte[16];
            Span<byte> span = arr;
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), SensorID);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), TimeStamp);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), SequenceNum);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12, 4), DataSize);
            return arr;
        }

        public static VideoPacketHeader FromBytes(byte[] arr)
        {
            if (arr.Length < 16) throw new ArgumentException("Array < 16 bytes");
            Span<byte> span = arr;
            return new VideoPacketHeader
            {
                SensorID = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(0, 4)),
                TimeStamp = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4)),
                SequenceNum = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4)),
                DataSize = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(12, 4))
            };
        }
    }
}