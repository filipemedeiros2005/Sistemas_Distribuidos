using OneHealth.Common;
using OneHealth.Sensor.Publishing;

namespace OneHealth.Sensor.Heartbeat;

/// <summary>
/// Publishes a <see cref="MsgType.Status"/> packet at a fixed interval, so
/// the gateway's watchdog knows the sensor is alive even between regular
/// DATA emissions. Designed to run as a background <see cref="Task"/>
/// alongside the main read loop.
///
/// Transient publish failures are logged but do not stop the loop —
/// missing a beat is the gateway's intended signal that something is wrong.
/// </summary>
public class HeartbeatTimer
{
    private readonly RabbitMqPublisher _publisher;
    private readonly uint _sensorId;
    private readonly TimeSpan _interval;

    public HeartbeatTimer(RabbitMqPublisher publisher, uint sensorId, TimeSpan interval)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _sensorId = sensorId;

        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(interval), "Heartbeat interval must be positive.");
        _interval = interval;
    }

    /// <summary>
    /// Runs the heartbeat loop until <paramref name="cancellationToken"/> is
    /// cancelled. Each tick publishes a STATUS / Heartbeat packet on the
    /// shared publisher channel.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        var count = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                count++;
                var packet = new TelemetryPacket
                {
                    SensorId  = _sensorId,
                    MsgType   = MsgType.Status,
                    DataType  = DataType.Heartbeat,
                    Value     = 0f,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                try
                {
                    await _publisher.PublishAsync(packet, cancellationToken);
                    Console.WriteLine($"[HEART #{count:D2}] STATUS sent.");
                }
                catch (OperationCanceledException)
                {
                    throw; // bubble up to outer try, exits the loop
                }
                catch (Exception ex)
                {
                    // Missing a beat is the gateway's signal — log and continue.
                    Console.Error.WriteLine($"[HEART] Publish failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}
