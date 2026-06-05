using OneHealth.Common;
using RabbitMQ.Client;

namespace OneHealth.Sensor.Publishing;

/// <summary>
/// Publishes <see cref="TelemetryPacket"/> payloads to the
/// <c>onehealth.telemetry</c> RabbitMQ topic exchange. Each packet is sent
/// with a routing key shaped as
/// <c>zone.&lt;ZONE&gt;.type.&lt;DATA_TYPE&gt;.sensor.&lt;ID&gt;</c>, which
/// lets gateways subscribe to topology-specific slices via wildcards.
///
/// Holds a single AMQP connection and channel for the lifetime of the sensor.
/// Disposing closes both gracefully.
/// </summary>
public sealed class RabbitMqPublisher : IAsyncDisposable
{
    public const string ExchangeName = "onehealth.telemetry";

    private readonly string _zone;
    private readonly uint _sensorId;
    private readonly string _hostName;

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqPublisher(string zone, uint sensorId, string hostName = "localhost")
    {
        if (string.IsNullOrWhiteSpace(zone))
            throw new ArgumentException("Zone must not be empty.", nameof(zone));
        if (string.IsNullOrWhiteSpace(hostName))
            throw new ArgumentException("HostName must not be empty.", nameof(hostName));

        _zone = zone;
        _sensorId = sensorId;
        _hostName = hostName;
    }

    /// <summary>
    /// Opens the AMQP connection and channel and declares the exchange.
    /// Must be called before <see cref="PublishAsync"/>.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = _hostName,
            UserName = "guest",
            Password = "guest"
        };

        _connection = await factory.CreateConnectionAsync(
            $"sensor-{_sensorId}", cancellationToken);

        _channel = await _connection.CreateChannelAsync(
            options: null, cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publishes a single packet. Messages are marked Persistent so the
    /// broker writes them to disk before acknowledging.
    /// </summary>
    public async Task PublishAsync(TelemetryPacket packet, CancellationToken cancellationToken = default)
    {
        if (_channel is null)
            throw new InvalidOperationException(
                "ConnectAsync must be called before PublishAsync.");

        var routingKey = BuildRoutingKey(packet);
        var body = packet.ToBytes();

        var properties = new BasicProperties
        {
            Persistent  = true,
            ContentType = "application/octet-stream",
            MessageId   = Guid.NewGuid().ToString("N")
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    private string BuildRoutingKey(TelemetryPacket packet)
    {
        var typeName = DataTypeMapping.ToName(packet.DataType);
        return $"zone.{_zone}.type.{typeName}.sensor.{_sensorId}";
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            try { await _channel.CloseAsync(); } catch { /* best-effort */ }
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            try { await _connection.CloseAsync(); } catch { /* best-effort */ }
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}