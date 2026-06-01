using Grpc.Core;
using OneHealth.Common;
using OneHealth.Grpc.Preprocessing;

namespace OneHealth.Preprocessor.Services;

/// <summary>
/// gRPC service that validates and normalizes individual telemetry measurements
/// on behalf of a Gateway. Stateless and idempotent: the same input always
/// produces the same output, regardless of call order or concurrency.
///
/// Sensor authorization is handled upstream by the Gateway (CSV allow-list),
/// so this service only deals with the data quality of an already-trusted
/// sensor. Applied checks, in order:
///   1. NaN / Infinity rejection (drop with reason "nan_or_inf")
///   2. Temperature unit normalization to °C (from "F" or "K" hints)
///   3. Physical bounds per data type (drop "out_of_bounds:&lt;TYPE&gt;")
///   4. Temporal sanity — timestamp not too far in the future ("future_timestamp")
/// </summary>
public class PreProcessorService : PreProcessor.PreProcessorBase
{
    /// <summary>
    /// Physical-plausibility bounds per canonical data-type name. A reading
    /// outside this (wider) range is treated as corrupt and dropped. Shared
    /// with the Sensor's anomaly limits via <see cref="MeasurementLimits"/>:
    /// the Sensor flags values outside the tighter "normal" band; this service
    /// drops only the truly implausible ones outside the "plausible" band.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, MeasurementLimits.Range> Bounds =
        MeasurementLimits.Plausible;

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
        if (Bounds.TryGetValue(dataType, out var range) && !range.Contains(value))
        {
            return Task.FromResult(Drop(request, $"out_of_bounds:{dataType}"));
        }

        // 4. Temporal sanity — only reject far-future timestamps. Old timestamps
        // are accepted because batch replay and queue backlogs are legitimate.
        var nowMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (request.UnixTs > nowMs + FutureToleranceMs)
            return Task.FromResult(Drop(request, "future_timestamp"));

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
