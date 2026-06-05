namespace OneHealth.Common;

/// <summary>
/// CRC-16/CCITT-FALSE checksum.
/// Parameters: polynomial = 0x1021, initial value = 0xFFFF, no input reflection, no final XOR.
/// </summary>
/// <remarks>
/// This is the de-facto standard CRC-16 variant in IoT and embedded protocols
/// (XMODEM, MultiTech, etc.). Strong enough for the 18-byte payload while staying cheap on tiny MCUs.
/// </remarks>
public static class Crc16
{
    private const ushort Polynomial   = 0x1021;
    private const ushort InitialValue = 0xFFFF;

    /// <summary>Computes the CRC-16/CCITT-FALSE of the given byte span.</summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = InitialValue;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ Polynomial);
                else
                    crc = (ushort)(crc << 1);
            }
        }
        return crc;
    }
}