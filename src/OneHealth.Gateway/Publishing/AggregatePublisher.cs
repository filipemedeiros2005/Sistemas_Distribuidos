using OneHealth.Common;
using RabbitMQ.Client;

namespace OneHealth.Gateway.Publishing;

/// <summary>
/// Publishes processed (and pre-processor-blessed) telemetry packets to the
/// second-stage exchange <c>onehealth.aggregated</c>. The Server will consume
/// from this exchange in Day 4 to persist measurements in PostgreSQL.
///
/// Routing key follows the same shape as the source exchange
/// (<c>zone.&lt;Z&gt;.type.&lt;T&gt;.sensor.&lt;ID&gt;</c>) so downstream
/// consumers can subscribe with topology-aware wildcards.
/// </summary>
public sealed class AggregatePublisher : IAsyncDisposable
{
    public const string ExchangeName = "onehealth.aggregated";

    private readonly int _gatewayPort;
    private readonly string _hostName;

    private IConnection? _connection;
    private IChannel? _channel;

    public AggregatePublisher(int gatewayPort, string hostName = "localhost")
    {
        if (string.IsNullOrWhiteSpace(hostName))
            throw new ArgumentException("HostName must not be empty.", nameof(hostName));

        _gatewayPort = gatewayPort;
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
            $"gateway-{_gatewayPort}-aggregator", cancellationToken);

        _channel = await _connection.CreateChannelAsync(
            options: null, cancellationToken: cancellationToken);

        // Same exchange topology rules as the source: topic, durable, no auto-delete.
        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publishes a packet to the aggregated exchange with the canonical
    /// routing key derived from its zone, type, and sensor id.
    /// </summary>
    public async Task PublishAsync(
        TelemetryPacket packet,
        string zone,
        CancellationToken cancellationToken = default)
    {
        if (_channel is null)
            throw new InvalidOperationException("ConnectAsync must be called before PublishAsync.");

        var typeName = DataTypeMapping.ToName(packet.DataType);
        var routingKey = $"zone.{zone}.type.{typeName}.sensor.{packet.SensorId}";
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
