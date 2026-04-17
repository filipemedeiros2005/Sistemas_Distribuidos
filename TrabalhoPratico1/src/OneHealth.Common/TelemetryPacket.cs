using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OneHealth.Common
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TelemetryPacket
    {
        public MsgType MsgType;     // 1 Byte
        public DataType DataType;   // 1 Byte
        public ushort Reserved;     // 2 Bytes
        public uint SensorID;       // 4 Bytes
        public uint TimeStamp;      // 4 Bytes
        public float Value;         // 4 Bytes
        public uint CheckSum;       // 4 Bytes

        public byte[] ToBytes()
        {
            byte[] arr = new byte[20];
            Span<byte> span = arr;

            span[0] = (byte)MsgType;
            span[1] = (byte)DataType;
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), Reserved);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), SensorID);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), TimeStamp);
            BinaryPrimitives.WriteSingleBigEndian(span.Slice(12, 4), Value);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(16, 4), CheckSum);

            return arr;
        }

        public static TelemetryPacket FromBytes(byte[] arr)
        {
            if (arr.Length < 20) throw new ArgumentException("Array < 20 bytes");
            Span<byte> span = arr;

            return new TelemetryPacket
            {
                MsgType = (MsgType)span[0],
                DataType = (DataType)span[1],
                Reserved = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2)),
                SensorID = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4)),
                TimeStamp = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4)),
                Value = BinaryPrimitives.ReadSingleBigEndian(span.Slice(12, 4)),
                CheckSum = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(16, 4))
            };
        }

        public void CalculateAndSetChecksum()
        {
            this.CheckSum = 0;
            byte[] dataRegion = new byte[16];
            Array.Copy(ToBytes(), 0, dataRegion, 0, 16);
            this.CheckSum = ComputeCRC32(dataRegion);
        }

        public bool IsValid()
        {
            byte[] dataRegion = new byte[16];
            Array.Copy(ToBytes(), 0, dataRegion, 0, 16);
            return ComputeCRC32(dataRegion) == this.CheckSum;
        }

        // Lógica CRC32 Simples embutida para não precisares de mais ficheiros
        private static uint ComputeCRC32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0) crc = (crc >> 1) ^ 0xEDB88320;
                    else crc >>= 1;
                }
            }
            return ~crc;
        }
    }
}