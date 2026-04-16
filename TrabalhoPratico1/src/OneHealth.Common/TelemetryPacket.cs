using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using OneHealth.Common.Helpers;

namespace OneHealth.Common
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TelemetryPacket
    {
        public MsgType MsgType;     // Offset 0
        public DataType DataType;   // Offset 1
        public ushort Reserved;     // Offset 2 (Padding to align)
        public uint SensorID;       // Offset 4
        public uint TimeStamp;      // Offset 8
        public float Value;         // Offset 12
        public uint CheckSum;       // Offset 16

        // Utils for bytes conversion using BinaryPrimitives for Big-Endian compliance
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
            if (arr.Length < 20) throw new ArgumentException("Array too short");
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
            this.CheckSum = 0; // zero before
            byte[] rawBytes = ToBytes();
            
            // CRC over the first 16 bytes
            byte[] dataRegion = new byte[16];
            Array.Copy(rawBytes, 0, dataRegion, 0, 16);
            
            this.CheckSum = Crc32Algorithm.Compute(dataRegion);
        }

        public bool IsValid()
        {
            byte[] rawBytes = ToBytes();
            byte[] dataRegion = new byte[16];
            Array.Copy(rawBytes, 0, dataRegion, 0, 16);
            
            uint computedCrc = Crc32Algorithm.Compute(dataRegion);
            return computedCrc == this.CheckSum;
        }
    }
}
