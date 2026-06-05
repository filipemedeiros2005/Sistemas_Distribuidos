namespace OneHealth.Gateway.Registry;

/// <summary>
/// Decides whether a sensor id is allowed through this gateway, checking it
/// against the allow-list loaded from the gateway's CSV configuration
/// (<c>gw_&lt;port&gt;.csv</c>). This is the single authorization gate: every
/// packet consumed from the broker is checked here before any further work.
///
/// <para>
/// Access is serialised with a <see cref="System.Threading.Mutex"/>. The
/// gateway consumes with a QoS prefetch greater than one, so the RabbitMQ
/// client can invoke the message handler on several thread-pool threads at
/// once; the mutex gives those concurrent lookups a single, consistent view
/// of the allow-list and keeps the door open for reloading it at runtime
/// without a data race. The mutex is unnamed (intra-process), so it behaves
/// the same on macOS, Linux, and Windows.
/// </para>
/// </summary>
public sealed class SensorAuthorizationGuard : IDisposable
{
    private readonly HashSet<uint> _allowed;
    private readonly Mutex _mutex = new(initiallyOwned: false);

    public SensorAuthorizationGuard(IEnumerable<uint> allowedSensorIds)
    {
        if (allowedSensorIds is null)
            throw new ArgumentNullException(nameof(allowedSensorIds));

        _allowed = new HashSet<uint>(allowedSensorIds);
    }

    /// <summary>
    /// Returns true if <paramref name="sensorId"/> is on the allow-list.
    /// </summary>
    public bool IsAuthorized(uint sensorId)
    {
        _mutex.WaitOne();
        try
        {
            return _allowed.Contains(sensorId);
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void Dispose() => _mutex.Dispose();
}
