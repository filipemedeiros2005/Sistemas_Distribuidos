using OneHealth.Grpc.Preprocessing;
using OneHealth.Preprocessor.Services;
using Xunit;

namespace OneHealth.Tests;

/// <summary>
/// Unit tests for <see cref="PreProcessorService.Normalize"/> covering the
/// four validations: NaN/Inf rejection, unit conversion, physical bounds,
/// and temporal sanity. The service is stateless, so a single shared instance
/// is reused across tests.
/// </summary>
public class PreProcessorServiceTests
{
    private static readonly PreProcessorService Service = new();
    private static readonly ulong NowMs =
        (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static RawMeasurement Req(
        string type, double value, string unit = "", ulong? ts = null) =>
        new()
        {
            SensorId = 101,
            DataType = type,
            Value    = value,
            UnitHint = unit,
            UnixTs   = ts ?? NowMs
        };

    // ---- Happy paths ----------------------------------------------------------

    [Fact]
    public async Task Valid_temperature_passes_through()
    {
        var resp = await Service.Normalize(Req("TEMP", 22.5), null!);

        Assert.False(resp.Dropped);
        Assert.Equal(22.5, resp.Value, precision: 4);
        Assert.Equal("TEMP", resp.DataType);
        Assert.Equal(101u, resp.SensorId);
    }

    [Fact]
    public async Task Fahrenheit_converts_to_celsius()
    {
        // 32°F = 0°C exactly
        var resp = await Service.Normalize(Req("TEMP", 32.0, unit: "F"), null!);

        Assert.False(resp.Dropped);
        Assert.Equal(0.0, resp.Value, precision: 4);
    }

    [Fact]
    public async Task Kelvin_converts_to_celsius()
    {
        // 273.15 K = 0°C
        var resp = await Service.Normalize(Req("TEMP", 273.15, unit: "K"), null!);

        Assert.False(resp.Dropped);
        Assert.Equal(0.0, resp.Value, precision: 4);
    }

    [Fact]
    public async Task Past_timestamp_is_accepted()
    {
        // Batch replay or queue backlog — legitimate.
        var pastTs = (ulong)DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var resp = await Service.Normalize(Req("TEMP", 22.5, ts: pastTs), null!);

        Assert.False(resp.Dropped);
    }

    [Fact]
    public async Task Unknown_data_type_passes_bounds_check()
    {
        // Unknown types skip bounds (upstream may have something we don't model).
        var resp = await Service.Normalize(Req("UNKNOWN_TYPE", 9999.0), null!);

        Assert.False(resp.Dropped);
    }

    // ---- Drop scenarios -------------------------------------------------------

    [Theory]
    [InlineData("TEMP",   80.0)]    // above +70
    [InlineData("TEMP",  -50.0)]    // below -40
    [InlineData("HUM",   110.0)]    // above 100
    [InlineData("HUM",    -5.0)]    // below 0
    [InlineData("NOISE", 150.0)]    // above 140
    [InlineData("PM25", 1500.0)]    // above 1000
    [InlineData("PM10", 1500.0)]
    [InlineData("LUM", 200_000.0)]  // above 150000
    public async Task Out_of_bounds_is_dropped(string type, double value)
    {
        var resp = await Service.Normalize(Req(type, value), null!);

        Assert.True(resp.Dropped);
        Assert.StartsWith("out_of_bounds:", resp.DropReason);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task NaN_or_infinity_is_dropped(double pathological)
    {
        var resp = await Service.Normalize(Req("TEMP", pathological), null!);

        Assert.True(resp.Dropped);
        Assert.Equal("nan_or_inf", resp.DropReason);
    }

    [Fact]
    public async Task Future_timestamp_beyond_tolerance_is_dropped()
    {
        // 5 minutes ahead of "now" — beyond the 1-minute drift tolerance.
        var futureTs = (ulong)DateTimeOffset.UtcNow
                              .AddMinutes(5)
                              .ToUnixTimeMilliseconds();
        var resp = await Service.Normalize(Req("TEMP", 22.5, ts: futureTs), null!);

        Assert.True(resp.Dropped);
        Assert.Equal("future_timestamp", resp.DropReason);
    }

    [Fact]
    public async Task Fahrenheit_high_value_dropped_after_conversion()
    {
        // 200°F = 93.3°C — above the 70°C upper bound.
        var resp = await Service.Normalize(Req("TEMP", 200.0, unit: "F"), null!);

        Assert.True(resp.Dropped);
        Assert.Equal("out_of_bounds:TEMP", resp.DropReason);
    }
}
