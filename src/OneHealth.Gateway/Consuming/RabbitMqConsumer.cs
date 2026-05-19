using OneHealth.Common;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OneHealth.Gateway.Consuming;

/// <summary>
/// Subscribes to telemetry packets on the <c>onehealth.telemetry</c> topic
/// exchange and dispatches each one to a handler. Owns the AMQP connection
/// and channel for the lifetime of the gateway process.
///
/// Routing keys consumed: <c>zone.&lt;ZONE&gt;.#</c> for every configured zone,
/// so the gateway receives everything from its topology without caring about
/// data types or sensor ids.
/// </summary>
/// <summary>
/// What the consumer should do with the current message after the handler
/// runs. Explicit so handlers don't need to communicate intent via exceptions.
/// </summary>
public enum ConsumeOutcome
{
    /// <summary>Successfully processed. Acknowledge to the broker.</summary>
    Ack,

    /// <summary>Transient failure (e.g. downstream service down). Requeue for a
    /// later attempt — RabbitMQ will re-deliver when this consumer is ready.</summary>
    RequeueAndRetry,

    /// <summary>Permanent failure (corrupt data, invalid sensor). Drop without
    /// requeue to avoid an infinite loop.</summary>
    DropPoison
}

public sealed class RabbitMqConsumer : IAsyncDisposable
{
    public const string ExchangeName = "onehealth.telemetry";

    private readonly int _gatewayPort;
    private readonly IReadOnlyList<string> _zones;
    private readonly string _hostName;

    private IConnection? _connection;
    private IChannel? _channel;
    private string? _queueName;

    public RabbitMqConsumer(int gatewayPort, IReadOnlyList<string> zones, string hostName = "localhost")
    {
        if (zones is null || zones.Count == 0)
            throw new ArgumentException("At least one zone must be supplied.", nameof(zones));
        if (string.IsNullOrWhiteSpace(hostName))
            throw new ArgumentException("HostName must not be empty.", nameof(hostName));

        _gatewayPort = gatewayPort;
        _zones = zones;
        _hostName = hostName;
    }

    /// <summary>
    /// Connects to RabbitMQ, declares the shared exchange (idempotent) and a
    /// gateway-specific durable queue, binds the queue to one routing pattern
    /// per zone, and configures QoS so at most 10 unacked messages flow at once.
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
            $"gateway-{_gatewayPort}", cancellationToken);

        _channel = await _connection.CreateChannelAsync(
            options: null, cancellationToken: cancellationToken);

        // Same declaration as the publisher — must agree on every property.
        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        // Durable queue named after the gateway so it survives broker restarts
        // and messages keep accumulating even while the gateway is down.
        var declareResult = await _channel.QueueDeclareAsync(
            queue: $"oh.gateway.{_gatewayPort}",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        _queueName = declareResult.QueueName;

        foreach (var zone in _zones)
        {
            var pattern = $"zone.{zone}.#";
            await _channel.QueueBindAsync(
                queue: _queueName,
                exchange: ExchangeName,
                routingKey: pattern,
                arguments: null,
                cancellationToken: cancellationToken);
            Console.WriteLine($"[BIND] {_queueName} <- {pattern}");
        }

        // QoS prefetch — limits in-flight unacked messages for backpressure.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 10,
            global: false,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Starts consuming from the bound queue. For each well-formed packet the
    /// handler is invoked and its returned <see cref="ConsumeOutcome"/> drives
    /// the ack/nack decision. Decoding failures (malformed payload) are dropped
    /// as poison — they would loop forever otherwise.
    /// </summary>
    public async Task ConsumeAsync(
        Func<TelemetryPacket, string, Task<ConsumeOutcome>> onMessage,
        CancellationToken cancellationToken)
    {
        if (_channel is null || _queueName is null)
            throw new InvalidOperationException("ConnectAsync must be called before ConsumeAsync.");

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            TelemetryPacket packet;
            try
            {
                packet = TelemetryPacket.FromBytes(ea.Body.Span);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[CONSUMER] Decode failure key={ea.RoutingKey}: {ex.Message}");
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            ConsumeOutcome outcome;
            try
            {
                outcome = await onMessage(packet, ea.RoutingKey);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[CONSUMER] Handler threw on key={ea.RoutingKey}: {ex.Message}");
                // Unexpected handler exception → drop as poison.
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            switch (outcome)
            {
                case ConsumeOutcome.Ack:
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    break;
                case ConsumeOutcome.RequeueAndRetry:
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    break;
                case ConsumeOutcome.DropPoison:
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    break;
            }
        };

        await _channel.BasicConsumeAsync(
            queue: _queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        // Keep the call alive until cancellation; the consumer runs on the
        // RabbitMQ.Client's internal scheduler.
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
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
