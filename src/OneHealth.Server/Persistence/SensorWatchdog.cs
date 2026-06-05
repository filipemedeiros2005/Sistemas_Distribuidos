using Npgsql;
using OneHealth.Common.Db;

namespace OneHealth.Server.Persistence;

/// <summary>
/// Background watchdog that turns the heartbeat signal into actual liveness
/// detection. Sensors publish a Status heartbeat every 30 s, which the Gateway
/// records as <c>last_seen</c>. This sweep periodically marks any ONLINE sensor
/// whose <c>last_seen</c> is older than <see cref="StaleThresholdSeconds"/> as
/// OFFLINE — so a sensor that crashes without sending a Bye is still detected.
/// </summary>
public sealed class SensorWatchdog : IAsyncDisposable
{
    /// <summary>Seconds without a heartbeat before a sensor is declared dead.
    /// Three missed 30 s beats.</summary>
    public const int StaleThresholdSeconds = 90;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly NpgsqlDataSource _dataSource;

    public SensorWatchdog(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>
    /// Runs the sweep on a fixed interval until cancelled. Each tick flips
    /// stale ONLINE sensors to OFFLINE and logs how many were affected.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await using var cmd = _dataSource.CreateCommand(SensorsSchema.MarkStaleSensorsOffline);
                    cmd.Parameters.AddWithValue(StaleThresholdSeconds);
                    var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
                    if (affected > 0)
                        Console.WriteLine($"[WATCHDOG] Marked {affected} stale sensor(s) OFFLINE.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A failed sweep is not fatal — log and try again next tick.
                    Console.Error.WriteLine($"[WATCHDOG] Sweep failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
