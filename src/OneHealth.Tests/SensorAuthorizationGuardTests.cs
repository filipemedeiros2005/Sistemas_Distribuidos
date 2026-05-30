extern alias gateway;
using gateway::OneHealth.Gateway.Registry;
using Xunit;

namespace OneHealth.Tests;

/// <summary>
/// Unit tests for <see cref="SensorAuthorizationGuard"/>, the gateway's single
/// authorization gate. The guard is the mutex-protected allow-list every packet
/// is checked against before the gateway does any further work.
/// </summary>
public class SensorAuthorizationGuardTests
{
    [Fact]
    public void Allowed_sensor_is_authorized()
    {
        using var guard = new SensorAuthorizationGuard(new uint[] { 101, 102 });

        Assert.True(guard.IsAuthorized(101));
        Assert.True(guard.IsAuthorized(102));
    }

    [Fact]
    public void Unknown_sensor_is_rejected()
    {
        using var guard = new SensorAuthorizationGuard(new uint[] { 101, 102 });

        Assert.False(guard.IsAuthorized(999));
    }

    [Fact]
    public void Empty_allow_list_rejects_everything()
    {
        using var guard = new SensorAuthorizationGuard(Array.Empty<uint>());

        Assert.False(guard.IsAuthorized(101));
    }

    [Fact]
    public void Concurrent_lookups_stay_consistent()
    {
        // Exercises the mutex: many threads hammering the guard at once must all
        // agree, with no torn reads of the underlying set.
        using var guard = new SensorAuthorizationGuard(new uint[] { 101, 102, 103, 104 });

        Parallel.For(0, 10_000, i =>
        {
            Assert.True(guard.IsAuthorized(101));
            Assert.False(guard.IsAuthorized(999));
        });
    }
}
