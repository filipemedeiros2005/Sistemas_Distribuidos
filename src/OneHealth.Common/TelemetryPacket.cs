using System.Buffers.Binary;

namespace OneHealth.Common;

/// <summary>
/// Fixed 20-byte binary packet exchanged between sensor, broker, gateway, and server.
/// All multi-byte fields are little-endian.
/// </summary>
/// <remarks>
/// Layout:
/// <code>
///   [00..03] SensorId   uint     (4 bytes)
///   [04]     MsgType    byte     (1 byte)
///   [05]     DataType   byte     (1 byte)
///   [06..09] Value      float    (4 bytes, IEEE 754)
///   [10..17] Timestamp  long     (8 bytes, Unix epoch ms)
///   [18..19] Checksum   ushort   (2 bytes, CRC-16/CCITT-FALSE over bytes [00..17])
/// </code>
/// The struct intentionally has public fields (no properties): it is a wire-level data
/// holder, not a domain object, and the verbosity of properties adds no value here.
/// </remarks>
public struct TelemetryPacket
{
    /// <summary>Exact serialized size in bytes.</summary>
    public const int PacketSize = 20;

    public uint     SensorId;
    public MsgType  MsgType;
    public DataType DataType;
    public float    Value;
    public long     Timestamp;

    /// <summary>Serializes this packet into a 20-byte little-endian buffer with CRC-16 checksum.</summary>
    public byte[] ToBytes()
    {
        var buffer = new byte[PacketSize];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), SensorId);
        span[4] = (byte)MsgType;
        span[5] = (byte)DataType;
        BinaryPrimitives.WriteSingleLittleEndian(span.Slice(6, 4), Value);
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(10, 8), Timestamp);

        var crc = Crc16.Compute(span.Slice(0, 18));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(18, 2), crc);

        return buffer;
    }

    /// <summary>Deserializes a 20-byte buffer and validates the CRC-16 checksum.</summary>
    /// <exception cref="ArgumentException">If the buffer length is not exactly 20 bytes.</exception>
    /// <exception cref="InvalidDataException">If the CRC-16 does not match.</exception>
    public static TelemetryPacket FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length != PacketSize)
            throw new ArgumentException($"Expected {PacketSize} bytes, got {data.Length}.", nameof(data));

        var receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(18, 2));
        var expectedCrc = Crc16.Compute(data.Slice(0, 18));
        if (receivedCrc != expectedCrc)
            throw new InvalidDataException($"CRC-16 mismatch: expected 0x{expectedCrc:X4}, got 0x{receivedCrc:X4}.");

        return new TelemetryPacket
        {
            SensorId  = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4)),
            MsgType   = (MsgType)data[4],
            DataType  = (DataType)data[5],
            Value     = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(6, 4)),
            Timestamp = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(10, 8))
        };
    }
}