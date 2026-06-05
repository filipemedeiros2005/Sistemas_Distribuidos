using Grpc.Net.Client;
using OneHealth.Common;
using OneHealth.Grpc.Preprocessing;

namespace OneHealth.Gateway.Preprocessing;

/// <summary>
/// Thin wrapper over the generated gRPC client. Holds a single
/// <see cref="GrpcChannel"/> for the lifetime of the gateway — channels are
/// designed for long-lived reuse and amortise HTTP/2 handshake cost over
/// thousands of calls.
///
/// Implements the design rule from section 2.5 of the architecture spec:
/// only packets with <see cref="MsgType.Data"/> are sent through Normalize.
/// ALERT, STATUS, HELLO and BYE bypass this client entirely — they are
/// forwarded downstream without pre-processing.
/// </summary>
public sealed class PreprocessorClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly PreProcessor.PreProcessorClient _client;
    private readonly TimeSpan _deadline;

    public PreprocessorClient(string address = "http://localhost:50051", TimeSpan? deadline = null)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address must not be empty.", nameof(address));

        _channel = GrpcChannel.ForAddress(address);
        _client  = new PreProcessor.PreProcessorClient(_channel);
        _deadline = deadline ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>
    /// Calls Normalize on the pre-processor. The deadline keeps the gateway
    /// responsive even when the upstream service is slow or stuck.
    /// </summary>
    public async Task<NormalizedMeasurement> NormalizeAsync(
        TelemetryPacket packet,
        CancellationToken cancellationToken = default)
    {
        var request = new RawMeasurement
        {
            SensorId = packet.SensorId,
            DataType = DataTypeMapping.ToName(packet.DataType),
            Value    = packet.Value,
            UnixTs   = (ulong)packet.Timestamp,
            UnitHint = string.Empty,   // simulated sensors already publish in canonical units
            MsgType  = (uint)packet.MsgType
        };

        return await _client.NormalizeAsync(
            request,
            deadline: DateTime.UtcNow.Add(_deadline),
            cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
