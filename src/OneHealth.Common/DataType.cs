namespace OneHealth.Common;

/// <summary>
/// One-byte physical quantity carried by a DATA or ALERT packet.
/// </summary>
public enum DataType : byte
{
    /// <summary>Temperature in degrees Celsius (canonical unit after pre-processing).</summary>
    Temperature = 0,

    /// <summary>Relative humidity in percent (0..100).</summary>
    Humidity    = 1,

    /// <summary>Particulate matter under 2.5 µm in µg/m³.</summary>
    Pm25        = 2,

    /// <summary>Particulate matter under 10 µm in µg/m³.</summary>
    Pm10        = 3,

    /// <summary>Sound level in decibels.</summary>
    Noise       = 4,

    /// <summary>Illuminance in lux.</summary>
    Luminosity  = 5,

    /// <summary>Special placeholder used together with MsgType.Status for heartbeats (no real measurement).</summary>
    Heartbeat   = 255
}