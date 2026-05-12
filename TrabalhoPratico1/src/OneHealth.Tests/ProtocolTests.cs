using OneHealth.Common;

namespace OneHealth.Tests;

public class TelemetryPacketTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new TelemetryPacket
        {
            MsgType = MsgType.ALERT,
            DataType = DataType.Temp,
            Reserved = 0,
            SensorID = 101,
            TimeStamp = 1_700_000_000,
            Value = 75.42f
        };
        original.CalculateAndSetChecksum();

        byte[] wire = original.ToBytes();
        Assert.Equal(20, wire.Length);

        var decoded = TelemetryPacket.FromBytes(wire);
        Assert.Equal(original.MsgType, decoded.MsgType);
        Assert.Equal(original.DataType, decoded.DataType);
        Assert.Equal(original.SensorID, decoded.SensorID);
        Assert.Equal(original.TimeStamp, decoded.TimeStamp);
        Assert.Equal(original.Value, decoded.Value);
        Assert.Equal(original.CheckSum, decoded.CheckSum);
        Assert.True(decoded.IsValid());
    }

    [Fact]
    public void IsValid_DetectsCorruption()
    {
        var pkt = new TelemetryPacket
        {
            MsgType = MsgType.DATA,
            DataType = DataType.Hum,
            SensorID = 102,
            TimeStamp = 1_700_000_001,
            Value = 50f
        };
        pkt.CalculateAndSetChecksum();
        byte[] wire = pkt.ToBytes();

        wire[12] ^= 0x55;

        var corrupted = TelemetryPacket.FromBytes(wire);
        Assert.False(corrupted.IsValid());
    }

    [Fact]
    public void BigEndian_HeaderLayoutMatchesSpec()
    {
        var pkt = new TelemetryPacket
        {
            MsgType = MsgType.HELO,
            DataType = DataType.Unknown,
            SensorID = 0x01020304,
            TimeStamp = 0x0A0B0C0D,
            Value = 0f
        };
        pkt.CalculateAndSetChecksum();
        byte[] wire = pkt.ToBytes();

        Assert.Equal(0x01, wire[4]);
        Assert.Equal(0x02, wire[5]);
        Assert.Equal(0x03, wire[6]);
        Assert.Equal(0x04, wire[7]);
        Assert.Equal(0x0A, wire[8]);
        Assert.Equal(0x0B, wire[9]);
        Assert.Equal(0x0C, wire[10]);
        Assert.Equal(0x0D, wire[11]);
    }
}

public class VideoPacketHeaderTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var h = new VideoPacketHeader
        {
            SensorID = 101,
            TimeStamp = 1_700_000_000,
            SequenceNum = 42,
            DataSize = 256
        };
        byte[] wire = h.ToBytes();
        Assert.Equal(16, wire.Length);

        var decoded = VideoPacketHeader.FromBytes(wire);
        Assert.Equal(h.SensorID, decoded.SensorID);
        Assert.Equal(h.TimeStamp, decoded.TimeStamp);
        Assert.Equal(h.SequenceNum, decoded.SequenceNum);
        Assert.Equal(h.DataSize, decoded.DataSize);
    }
}

public class TopicTests
{
    [Theory]
    [InlineData("ZONA_NORTE", DataType.Temp, 101u, "zone.ZONA_NORTE.type.TEMP.sensor.101")]
    [InlineData("ZONA_SUL", DataType.PM25, 102u, "zone.ZONA_SUL.type.PM25.sensor.102")]
    [InlineData("Zona Industrial", DataType.Ruido, 103u, "zone.ZONA_INDUSTRIAL.type.RUIDO.sensor.103")]
    public void ToRoutingKey_FormatsCorrectly(string zona, DataType dataType, uint sensorId, string expected)
    {
        Assert.Equal(expected, Topic.ToRoutingKey(zona, dataType, sensorId));
    }

    [Fact]
    public void TryParse_RoundTripsKey()
    {
        var key = Topic.ToRoutingKey("ZONA_NORTE", DataType.Hum, 999);
        bool ok = Topic.TryParse(key, out var zona, out var dataType, out var sensorId);
        Assert.True(ok);
        Assert.Equal("ZONA_NORTE", zona);
        Assert.Equal("HUM", dataType);
        Assert.Equal(999u, sensorId);
    }

    [Fact]
    public void TryParse_RejectsMalformedKey()
    {
        Assert.False(Topic.TryParse("not.a.valid.key", out _, out _, out _));
        Assert.False(Topic.TryParse("", out _, out _, out _));
        Assert.False(Topic.TryParse("zone.X.type.Y.sensor.NOT_A_NUMBER", out _, out _, out _));
    }

    [Fact]
    public void ZoneBindingPattern_NormalizesZone()
    {
        Assert.Equal("zone.ZONA_NORTE.#", Topic.ZoneBindingPattern("zona_norte"));
        Assert.Equal("zone.ZONA_INDUSTRIAL.#", Topic.ZoneBindingPattern("Zona Industrial"));
    }
}
