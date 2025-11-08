namespace NSerf.Memberlist.Common;

/// <summary>
/// Simple CRC32 implementation.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        const uint poly = 0xedb88320;
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ poly;
                else
                    crc >>= 1;
            }
            table[i] = crc;
        }

        return table;
    }

    public static uint Compute(byte[] buffer, int offset, int count)
    {
        var crc = 0xFFFFFFFF;

        for (var i = offset; i < offset + count; i++)
        {
            var index = (byte)((crc & 0xFF) ^ buffer[i]);
            crc = (crc >> 8) ^ Table[index];
        }

        return ~crc;
    }
}