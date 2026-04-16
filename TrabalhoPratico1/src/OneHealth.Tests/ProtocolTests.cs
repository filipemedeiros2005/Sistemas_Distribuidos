using System;
using System.Runtime.InteropServices;
using Xunit;
using OneHealth.Common;

namespace OneHealth.Tests
{
    public class ProtocolTests
    {
        [Fact]
        public void TelemetryPacket_ShouldBe_Exactly20Bytes()
        {
            int size = Marshal.SizeOf(typeof(TelemetryPacket));
            Assert.Equal(20, size);
        }

        [Fact]
        public void VideoPacketHeader_ShouldBe_Exactly16Bytes()
        {
            int size = Marshal.SizeOf(typeof(VideoPacketHeader));
            Assert.Equal(16, size);
        }

        [Fact]
        public void TelemetryPacket_Serialization_And_Deserialization_ShouldMatch()
        {
            var packet = new TelemetryPacket
            {
                MsgType = MsgType.DATA,
                DataType = DataType.Temp,
                Reserved = 0,
                SensorID = 1001,
                TimeStamp = 1681640000,
                Value = 25.4f,
                CheckSum = 0
            };

            packet.CalculateAndSetChecksum();
            byte[] rawBytes = packet.ToBytes();

            Assert.Equal(20, rawBytes.Length);

            var deserialized = TelemetryPacket.FromBytes(rawBytes);

            Assert.Equal(packet.MsgType, deserialized.MsgType);
            Assert.Equal(packet.DataType, deserialized.DataType);
            Assert.Equal(packet.Reserved, deserialized.Reserved);
            Assert.Equal(packet.SensorID, deserialized.SensorID);
            Assert.Equal(packet.TimeStamp, deserialized.TimeStamp);
            Assert.Equal(packet.Value, deserialized.Value);
            
            Assert.True(deserialized.IsValid());
        }
    }
}

