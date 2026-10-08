using System.IO;
using System.IO.Compression;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Декодирует транспортные PDF stream filters, используемые перед raster/JPEG данными.
/// </summary>
internal static class PdfStreamFilterDecoder
{
    /// <summary>
    /// Декодирует Flate/zlib stream.
    /// </summary>
    public static byte[] DecodeFlate(byte[] data)
    {
        using var input = new MemoryStream(
            data,
            writable: false);
        using var zlib = new ZLibStream(
            input,
            CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Декодирует ASCIIHexDecode до end-of-data marker >.
    /// </summary>
    public static byte[] DecodeAsciiHex(byte[] data)
    {
        var output = new List<byte>();
        int? highNibble = null;
        var foundEnd = false;

        foreach (var value in data)
        {
            if (IsWhiteSpace(value))
            {
                continue;
            }

            if (value == (byte)'>')
            {
                foundEnd = true;
                break;
            }

            var nibble = ReadHexNibble(value);

            if (highNibble == null)
            {
                highNibble = nibble;
            }
            else
            {
                output.Add(
                    (byte)((highNibble.Value << 4) |
                           nibble));
                highNibble = null;
            }
        }

        if (!foundEnd)
        {
            throw new InvalidDataException(
                "PDF ASCIIHexDecode не содержит end marker >.");
        }

        if (highNibble != null)
        {
            output.Add(
                (byte)(highNibble.Value << 4));
        }

        return output.ToArray();
    }

    /// <summary>
    /// Декодирует ASCII85Decode до end-of-data marker ~>.
    /// Поддерживает сокращение z и необязательный префикс <~.
    /// </summary>
    public static byte[] DecodeAscii85(byte[] data)
    {
        var output = new List<byte>();
        var group = new List<byte>(5);
        var position = 0;
        var foundEnd = false;

        SkipWhiteSpace(
            data,
            ref position);

        if (position + 1 < data.Length &&
            data[position] == (byte)'<' &&
            data[position + 1] == (byte)'~')
        {
            position += 2;
        }

        while (position < data.Length)
        {
            var value = data[position++];

            if (IsWhiteSpace(value))
            {
                continue;
            }

            if (value == (byte)'~')
            {
                SkipWhiteSpace(
                    data,
                    ref position);

                if (position >= data.Length ||
                    data[position] != (byte)'>')
                {
                    throw new InvalidDataException(
                        "PDF ASCII85Decode содержит некорректный end marker.");
                }

                position++;
                foundEnd = true;
                break;
            }

            if (value == (byte)'z')
            {
                if (group.Count != 0)
                {
                    throw new InvalidDataException(
                        "PDF ASCII85Decode использует z внутри группы.");
                }

                output.AddRange(
                    new byte[] { 0, 0, 0, 0 });
                continue;
            }

            if (value is < (byte)'!' or > (byte)'u')
            {
                throw new InvalidDataException(
                    $"PDF ASCII85Decode содержит недопустимый byte 0x{value:X2}.");
            }

            group.Add(value);

            if (group.Count == 5)
            {
                AppendAscii85Group(
                    output,
                    group,
                    4);
                group.Clear();
            }
        }

        if (!foundEnd)
        {
            throw new InvalidDataException(
                "PDF ASCII85Decode не содержит end marker ~>.");
        }

        if (group.Count == 1)
        {
            throw new InvalidDataException(
                "PDF ASCII85Decode содержит неполную группу из одного символа.");
        }

        if (group.Count > 1)
        {
            var outputBytes = group.Count - 1;

            while (group.Count < 5)
            {
                group.Add((byte)'u');
            }

            AppendAscii85Group(
                output,
                group,
                outputBytes);
        }

        return output.ToArray();
    }

    private static void AppendAscii85Group(
        List<byte> output,
        IReadOnlyList<byte> group,
        int outputBytes)
    {
        ulong value = 0;

        for (var index = 0; index < 5; index++)
        {
            value = checked(
                value * 85 +
                (uint)(group[index] - (byte)'!'));
        }

        if (value > uint.MaxValue)
        {
            throw new InvalidDataException(
                "PDF ASCII85Decode group переполняет 32-bit значение.");
        }

        var decoded = new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        };

        for (var index = 0; index < outputBytes; index++)
        {
            output.Add(decoded[index]);
        }
    }

    private static void SkipWhiteSpace(
        byte[] data,
        ref int position)
    {
        while (position < data.Length &&
               IsWhiteSpace(data[position]))
        {
            position++;
        }
    }

    private static bool IsWhiteSpace(byte value)
    {
        return value is
            0x00 or
            0x09 or
            0x0A or
            0x0C or
            0x0D or
            0x20;
    }

    private static int ReadHexNibble(byte value)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            return value - (byte)'0';
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            return value - (byte)'A' + 10;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            return value - (byte)'a' + 10;
        }

        throw new InvalidDataException(
            $"PDF ASCIIHexDecode содержит недопустимую hex-цифру 0x{value:X2}.");
    }
}
