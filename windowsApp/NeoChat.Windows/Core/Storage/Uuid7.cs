using System.Security.Cryptography;

namespace NeoChat.Windows.Core.Storage;

/// <summary>
/// UUIDv7 (RFC 9562) as the 36-character lowercase text the frozen DDL prescribes for every primary
/// key: the first 48 bits are a UTC millisecond timestamp, so ids sort by creation time and a keyset
/// page over an id column stays chronological.
/// </summary>
public static class Uuid7
{
    private const int TimestampBytes = 6;

    public static string NewId() => NewId(DateTimeOffset.UtcNow);

    /// <summary>Deterministic on the clock but random in the tail, so tests can pin ordering.</summary>
    public static string NewId(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        var milliseconds = timestamp.ToUnixTimeMilliseconds();
        for (var index = TimestampBytes - 1; index >= 0; index--)
        {
            bytes[index] = (byte)(milliseconds & 0xFF);
            milliseconds >>= 8;
        }

        RandomNumberGenerator.Fill(bytes[TimestampBytes..]);

        // version 7 in the high nibble of byte 6, RFC 9562 variant 10 in the high bits of byte 8
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return string.Create(36, bytes.ToArray(), static (chars, value) =>
        {
            const string Hex = "0123456789abcdef";
            var cursor = 0;
            for (var index = 0; index < 16; index++)
            {
                if (index is 4 or 6 or 8 or 10)
                {
                    chars[cursor++] = '-';
                }

                chars[cursor++] = Hex[value[index] >> 4];
                chars[cursor++] = Hex[value[index] & 0x0F];
            }
        });
    }

    /// <summary>
    /// Milliseconds encoded in an id, for diagnostics and tests; 0 when the text is malformed. A v7
    /// timestamp is 48 bits, which is the eight hex digits before the first dash plus four after it.
    /// </summary>
    public static long TimestampOf(string id)
    {
        if (id.Length < 13 || id[8] != '-')
        {
            return 0;
        }

        long milliseconds = 0;
        for (var index = 0; index < 8; index++)
        {
            var digit = HexValue(id[index]);
            if (digit < 0)
            {
                return 0;
            }

            milliseconds = (milliseconds << 4) | (uint)digit;
        }

        for (var index = 9; index < 13; index++)
        {
            var digit = HexValue(id[index]);
            if (digit < 0)
            {
                return 0;
            }

            milliseconds = (milliseconds << 4) | (uint)digit;
        }

        return milliseconds;
    }

    private static int HexValue(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => -1,
    };
}
