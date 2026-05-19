using Microsoft.Extensions.DependencyInjection;
using Grpc.Core;
using OneHealth.Grpc.Preprocessing;
using OneHealth.Preprocessor.Authorization;

namespace OneHealth.Preprocessor.Services;

/// <summary>
/// gRPC service that validates and normalizes individual telemetry measurements
/// on behalf of a Gateway. Stateless and idempotent: the same input always
/// produces the same output, regardless of call order or concurrency.
///
/// Applied checks, in order:
///   1. NaN / Infinity rejection (drop with reason "nan_or_inf")
///   2. Temperature unit normalization to °C (from "F" or "K" hints)
///   3. Physical bounds per data type (drop "out_of_bounds:&lt;TYPE&gt;")
///   4. Temporal sanity — timestamp not too far in the future ("future_timestamp")
///   5. Sensor authorization — sensor id must be registered in the
///      <c>sensors</c> table populated by the Gateway. Skipped if no
///      <see cref="SensorAuthorizationCache"/> is configured (unit tests).
/// </summary>
public class PreProcessorService : PreProcessor.PreProcessorBase
{
    private readonly SensorAuthorizationCache? _authCache;

    /// <summary>
    /// Production constructor — enforces authorization via the cache.
    /// Marked with <see cref="ActivatorUtilitiesConstructorAttribute"/> so
    /// ASP.NET Core's DI picks this overload over the parameterless one
    /// (the latter exists only for unit tests).
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public PreProcessorService(SensorAuthorizationCache authCache)
    {
        _authCache = authCache;
    }

    /// <summary>Test constructor — authorization check is skipped.</summary>
    public PreProcessorService()
    {
        _authCache = null;
    }

    /// <summary>
    /// Inclusive (min, max) ranges per canonical data-type name.
    /// Values converted to the canonical unit (°C for TEMP) are compared
    /// against these bounds.
    /// </summary>
    private static readonly Dictionary<string, (double Min, double Max)> Bounds = new()
    {
        ["TEMP"]  = (-40.0, 70.0),
        ["HUM"]   = (0.0, 100.0),
        ["PM25"]  = (0.0, 1000.0),
        ["PM10"]  = (0.0, 1000.0),
        ["NOISE"] = (0.0, 140.0),
        ["LUM"]   = (0.0, 150_000.0)
    };

    /// <summary>Future-timestamp tolerance, accounting for mild clock drift.</summary>
    private const ulong FutureToleranceMs = 60_000; // 1 minute

    public override Task<NormalizedMeasurement> Normalize(
        RawMeasurement request, ServerCallContext context)
    {
        // 1. NaN / Infinity — never let pathological floats reach the database.
        if (double.IsNaN(request.Value) || double.IsInfinity(request.Value))
            return Task.FromResult(Drop(request, "nan_or_inf"));

        var value = request.Value;
        var dataType = (request.DataType ?? string.Empty).ToUpperInvariant();

        // 2. Unit normalization (TEMP only).
        if (dataType == "TEMP")
        {
            value = (request.UnitHint ?? string.Empty).ToUpperInvariant() switch
            {
                "F" => (value - 32.0) * 5.0 / 9.0,
                "K" => value - 273.15,
                _   => value  // empty / "C" / unknown: assume already in Celsius
            };

            // Recheck NaN after conversion (defensive — shouldn't happen with finite inputs).
            if (double.IsNaN(value) || double.IsInfinity(value))
                return Task.FromResult(Drop(request, "nan_or_inf_after_unit_conversion"));
        }

        // 3. Physical bounds.
        if (Bounds.TryGetValue(dataType, out var range) &&
            (value < range.Min || value > range.Max))
        {
            return Task.FromResult(Drop(request, $"out_of_bounds:{dataType}"));
        }

        // 4. Temporal sanity — only reject far-future timestamps. Old timestamps
        // are accepted because batch replay and queue backlogs are legitimate.
        var nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (request.UnixTs > nowMs + FutureToleranceMs)
            return Task.FromResult(Drop(request, "future_timestamp"));

        // 5. Sensor authorization — only sensors registered by the Gateway
        // (via Hello/Status) are allowed through. Skipped in tests where no
        // cache is configured.
        if (_authCache is not null && !_authCache.IsAuthorized(request.SensorId))
            return Task.FromResult(Drop(request, "unauthorized_sensor"));

        return Task.FromResult(new NormalizedMeasurement
        {
            SensorId = request.SensorId,
            DataType = request.DataType,
            UnixTs   = request.UnixTs,
            Value    = value,
            Dropped  = false
        });
    }

    /// <summary>Builds a "dropped" response while echoing the request's identifiers.</summary>
    private static NormalizedMeasurement Drop(RawMeasurement request, string reason) =>
        new()
        {
            SensorId   = request.SensorId,
            DataType   = request.DataType,
            UnixTs     = request.UnixTs,
            Value      = 0,
            Dropped    = true,
            DropReason = reason
        };
}
