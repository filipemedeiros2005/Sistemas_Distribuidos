using OneHealth.Common;

namespace OneHealth.Tests;

public class TelemetryPacketTests
{
    private static TelemetryPacket SampleDataPacket() => new()
    {
        SensorId  = 101,
        MsgType   = MsgType.Data,
        DataType  = DataType.Temperature,
        Value     = 23.5f,
        Timestamp = 1_716_000_000_000L
    };

    [Fact]
    public void ToBytes_ProducesExactlyTwentyBytes()
    {
        var bytes = SampleDataPacket().ToBytes();
        Assert.Equal(TelemetryPacket.PacketSize, bytes.Length);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = SampleDataPacket();
        var restored = TelemetryPacket.FromBytes(original.ToBytes());

        Assert.Equal(original.SensorId,  restored.SensorId);
        Assert.Equal(original.MsgType,   restored.MsgType);
        Assert.Equal(original.DataType,  restored.DataType);
        Assert.Equal(original.Value,     restored.Value);
        Assert.Equal(original.Timestamp, restored.Timestamp);
    }

    [Fact]
    public void FromBytes_ThrowsWhenLengthIsWrong()
    {
        var tooShort = new byte[19];
        Assert.Throws<ArgumentException>(() => TelemetryPacket.FromBytes(tooShort));
    }

    [Fact]
    public void FromBytes_ThrowsWhenChecksumIsTampered()
    {
        var bytes = SampleDataPacket().ToBytes();
        bytes[7] ^= 0xFF; // Flip bits in the Value field — checksum is now stale

        Assert.Throws<InvalidDataException>(() => TelemetryPacket.FromBytes(bytes));
    }

    [Fact]
    public void ToBytes_UsesLittleEndianForSensorId()
    {
        var packet = new TelemetryPacket { SensorId = 0x01020304 };
        var bytes = packet.ToBytes();

        // Little-endian: least significant byte at the lowest address
        Assert.Equal(0x04, bytes[0]);
        Assert.Equal(0x03, bytes[1]);
        Assert.Equal(0x02, bytes[2]);
        Assert.Equal(0x01, bytes[3]);
    }
}