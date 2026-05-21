using OneHealth.Common;

namespace OneHealth.Tests;

/// <summary>
/// Regression tests for <see cref="TelemetryPacket"/>. Together they pin
/// down the wire contract: every sensor, every gateway, and every server on
/// the network must agree on the exact 20-byte layout and the CRC-16
/// integrity check, otherwise the system silently corrupts measurements.
/// </summary>
public class TelemetryPacketTests
{
    /// <summary>Reusable canonical packet for the happy-path tests.</summary>
    private static TelemetryPacket SampleDataPacket() => new()
    {
        SensorId  = 101,
        MsgType   = MsgType.Data,
        DataType  = DataType.Temperature,
        Value     = 23.5f,
        Timestamp = 1_716_000_000_000L
    };

    /// <summary>Locks in the wire size so adding a field is a deliberate decision.</summary>
    [Fact]
    public void ToBytes_ProducesExactlyTwentyBytes()
    {
        var bytes = SampleDataPacket().ToBytes();
        Assert.Equal(TelemetryPacket.PacketSize, bytes.Length);
    }

    /// <summary>Serialise-then-deserialise must yield a structurally identical packet.</summary>
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

    /// <summary>Truncated buffers are rejected before any CRC work is attempted.</summary>
    [Fact]
    public void FromBytes_ThrowsWhenLengthIsWrong()
    {
        var tooShort = new byte[19];
        Assert.Throws<ArgumentException>(() => TelemetryPacket.FromBytes(tooShort));
    }

    /// <summary>
    /// A single bit-flip inside the payload must invalidate the stored CRC-16
    /// and cause <see cref="TelemetryPacket.FromBytes"/> to refuse the packet —
    /// this is what catches in-flight corruption between broker and consumer.
    /// </summary>
    [Fact]
    public void FromBytes_ThrowsWhenChecksumIsTampered()
    {
        var bytes = SampleDataPacket().ToBytes();
        bytes[7] ^= 0xFF; // Flip bits in the Value field — checksum is now stale.

        Assert.Throws<InvalidDataException>(() => TelemetryPacket.FromBytes(bytes));
    }

    /// <summary>
    /// Sanity check on endianness — the protocol is explicitly little-endian
    /// so that x86 and ARM machines exchange packets without per-machine
    /// byte-swapping logic.
    /// </summary>
    [Fact]
    public void ToBytes_UsesLittleEndianForSensorId()
    {
        var packet = new TelemetryPacket { SensorId = 0x01020304 };
        var bytes = packet.ToBytes();

        // Little-endian: least significant byte at the lowest address.
        Assert.Equal(0x04, bytes[0]);
        Assert.Equal(0x03, bytes[1]);
        Assert.Equal(0x02, bytes[2]);
        Assert.Equal(0x01, bytes[3]);
    }
}
