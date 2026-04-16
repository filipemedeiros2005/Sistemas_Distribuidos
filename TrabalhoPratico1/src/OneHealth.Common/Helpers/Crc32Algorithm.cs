using System;
using System.Collections.Generic;

namespace OneHealth.Common.Helpers
{
    public static class Crc32Algorithm
    {
        private const uint Polynomial = 0xedb88320;
        private static readonly uint[] Table;

        static Crc32Algorithm()
        {
            Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint entry = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((entry & 1) == 1)
                        entry = (entry >> 1) ^ Polynomial;
                    else
                        entry >>= 1;
                }
                Table[i] = entry;
            }
        }

        public static uint Compute(byte[] buffer)
        {
            uint crc = ~0u;
            for (int i = 0; i < buffer.Length; i++)
            {
                byte index = (byte)((crc & 0xff) ^ buffer[i]);
                crc = (crc >> 8) ^ Table[index];
            }
            return ~crc;
        }
    }
}

