using System.IO;
using System.IO.Compression;
using System.Text;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Представляет подготовленный PDF Image XObject и опциональный grayscale soft mask
/// в форматах, которые можно встроить в SVG/HTML как data URI.
/// </summary>
internal sealed class PdfImageResource
{
    public required byte[] Data { get; init; }
    public required string ContentType { get; init; }
    public byte[]? SoftMaskData { get; init; }
    public string? SoftMaskContentType { get; init; }
}

/// <summary>
/// Создаёт минимальный PNG из 8-bit DeviceGray/DeviceRGB raster data.
/// </summary>
internal static class PdfPngEncoder
{
    /// <summary>
    /// Кодирует raster без alpha и без PDF predictor в PNG.
    /// </summary>
    public static byte[] Encode(
        byte[] pixels,
        int width,
        int height,
        int channels)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException(
                "PDF image имеет недопустимый размер.");
        }

        if (channels is not 1 and not 3)
        {
            throw new InvalidDataException(
                "PNG encoder поддерживает только DeviceGray и DeviceRGB.");
        }

        var rowBytes = checked(width * channels);
        var expectedLength = checked(rowBytes * height);

        if (pixels.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"PDF raster содержит {pixels.Length} bytes вместо ожидаемых {expectedLength}.");
        }

        using var output = new MemoryStream();
        output.Write(new byte[]
        {
            137, 80, 78, 71, 13, 10, 26, 10
        });

        Span<byte> ihdr = stackalloc byte[13];
        WriteUInt32BigEndian(ihdr[..4], checked((uint)width));
        WriteUInt32BigEndian(ihdr.Slice(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = channels == 1 ? (byte)0 : (byte)2;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);

        using var raw = new MemoryStream(
            checked((rowBytes + 1) * height));

        for (var row = 0; row < height; row++)
        {
            raw.WriteByte(0);
            raw.Write(
                pixels,
                row * rowBytes,
                rowBytes);
        }

        raw.Position = 0;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(
                   compressed,
                   CompressionLevel.Optimal,
                   leaveOpen: true))
        {
            raw.CopyTo(zlib);
        }

        WriteChunk(
            output,
            "IDAT",
            compressed.ToArray());

        WriteChunk(
            output,
            "IEND",
            ReadOnlySpan<byte>.Empty);

        return output.ToArray();
    }

    private static void WriteChunk(
        Stream output,
        string type,
        ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteUInt32BigEndian(
            length,
            checked((uint)data.Length));
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(
        ReadOnlySpan<byte> type,
        ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in type)
        {
            crc = UpdateCrc32(crc, value);
        }

        foreach (var value in data)
        {
            crc = UpdateCrc32(crc, value);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint UpdateCrc32(
        uint crc,
        byte value)
    {
        crc ^= value;

        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0
                ? 0xEDB88320u ^ (crc >> 1)
                : crc >> 1;
        }

        return crc;
    }

    private static void WriteUInt32BigEndian(
        Span<byte> destination,
        uint value)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }
}
