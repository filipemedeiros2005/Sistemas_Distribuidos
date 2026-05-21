using OneHealth.Common;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OneHealth.Server.Aggregation;

/// <summary>
/// Subscribes to the <c>onehealth.aggregated</c> exchange — populated by
/// every Gateway after pre-processing. The Server's job is to persist every
/// packet that lands here into PostgreSQL.
///
/// The queue is durable and bound with <c>#</c> (catch-all) so the Server
/// gets the entire firehose regardless of zone or data type.
/// </summary>
public sealed class AggregateConsumer : IAsyncDisposable
{
    public const string ExchangeName = "onehealth.aggregated";
    public const string QueueName    = "oh.server.aggregated";

    private readonly string _hostName;
    private IConnection? _connection;
    private IChannel?    _channel;

    public AggregateConsumer(string hostName = "localhost")
    {
        if (string.IsNullOrWhiteSpace(hostName))
            throw new ArgumentException("HostName must not be empty.", nameof(hostName));

        _hostName = hostName;
    }

    /// <summary>
    /// Opens the connection and channel, declares the exchange (idempotent
    /// — matches the Gateway's declaration), declares a durable server-owned
    /// queue, binds it with the catch-all pattern, and sets QoS prefetch.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = _hostName,
            UserName = "guest",
            Password = "guest"
        };

        _connection = await factory.CreateConnectionAsync("server-aggregator", cancellationToken);
        _channel    = await _connection.CreateChannelAsync(options: null, cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await _channel.QueueBindAsync(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: "#",          // catch-all
            arguments: null,
            cancellationToken: cancellationToken);

        await _channel.BasicQosAsync(
            prefetchSize: 0, prefetchCount: 20, global: false,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Starts consuming. Each successfully handled packet is acked; decode
    /// failures and handler exceptions are dropped as poison.
    /// </summary>
    public async Task ConsumeAsync(
        Func<TelemetryPacket, string, Task> onMessage,
        CancellationToken cancellationToken)
    {
        if (_channel is null)
            throw new InvalidOperationException("ConnectAsync must be called before ConsumeAsync.");

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var packet = TelemetryPacket.FromBytes(ea.Body.Span);
                await onMessage(packet, ea.RoutingKey);
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[AGG-CONSUMER] Drop poison key={ea.RoutingKey}: {ex.Message}");
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        try { await Task.Delay(Timeout.Infinite, cancellationToken); }
        catch (OperationCanceledException) { /* expected */ }
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
