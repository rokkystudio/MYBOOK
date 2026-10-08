using System.IO;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Преобразует поддерживаемые PDF raster color samples в RGB для PNG renderer.
/// </summary>
internal static class PdfImageColorConverter
{
    /// <summary>
    /// Применяет PDF /Decode pairs к 8-bit component samples.
    /// </summary>
    public static byte[] ApplyComponentDecode(
        byte[] samples,
        int components,
        IReadOnlyList<double> decode)
    {
        if (components <= 0 ||
            samples.Length % components != 0)
        {
            throw new InvalidDataException(
                "PDF raster имеет некорректное число component samples.");
        }

        if (decode.Count != components * 2)
        {
            throw new InvalidDataException(
                "PDF /Decode не совпадает с числом color components.");
        }

        var output = new byte[samples.Length];

        for (var index = 0; index < samples.Length; index++)
        {
            var component = index % components;
            var minimum = decode[component * 2];
            var maximum = decode[component * 2 + 1];

            var mapped =
                minimum +
                samples[index] / 255.0 *
                (maximum - minimum);

            output[index] = ToByte(mapped);
        }

        return output;
    }

    /// <summary>
    /// Убирает matte premultiplication из 8-bit component samples
    /// по декодированным alpha samples soft mask.
    /// </summary>
    public static byte[] RemoveMatte(
        byte[] samples,
        int components,
        byte[] alphaSamples,
        IReadOnlyList<double> matte)
    {
        if (components <= 0 ||
            samples.Length % components != 0)
        {
            throw new InvalidDataException(
                "PDF raster имеет некорректное число component samples.");
        }

        var pixelCount = samples.Length / components;

        if (alphaSamples.Length != pixelCount)
        {
            throw new InvalidDataException(
                "PDF /SMask alpha samples не совпадают с числом pixels основного изображения.");
        }

        if (matte.Count != components)
        {
            throw new InvalidDataException(
                "PDF /SMask /Matte не совпадает с числом color components.");
        }

        var output = new byte[samples.Length];

        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var alpha = alphaSamples[pixel] / 255.0;

            for (var component = 0; component < components; component++)
            {
                var source =
                    samples[pixel * components + component] /
                    255.0;

                double value;

                if (alpha <= 0)
                {
                    value = 0;
                }
                else
                {
                    var matteValue = matte[component];
                    value =
                        (source -
                         (1 - alpha) * matteValue) /
                        alpha;
                }

                output[pixel * components + component] =
                    ToByte(value);
            }
        }

        return output;
    }

    /// <summary>
    /// Преобразует 8-bit DeviceCMYK samples в RGB.
    /// </summary>
    public static byte[] ConvertCmykToRgb(byte[] samples)
    {
        if (samples.Length % 4 != 0)
        {
            throw new InvalidDataException(
                "PDF DeviceCMYK raster имеет неполный pixel.");
        }

        var output = new byte[checked(samples.Length / 4 * 3)];

        for (int source = 0, target = 0;
             source < samples.Length;
             source += 4, target += 3)
        {
            var c = samples[source] / 255.0;
            var m = samples[source + 1] / 255.0;
            var y = samples[source + 2] / 255.0;
            var k = samples[source + 3] / 255.0;

            output[target] = ToByte(
                1 - Math.Min(1, c + k));
            output[target + 1] = ToByte(
                1 - Math.Min(1, m + k));
            output[target + 2] = ToByte(
                1 - Math.Min(1, y + k));
        }

        return output;
    }

    /// <summary>
    /// Распаковывает 1/2/4/8-bit Indexed samples по строкам
    /// и заменяет индексы готовыми RGB palette entries.
    /// </summary>
    public static byte[] ConvertIndexedToRgb(
        byte[] samples,
        int width,
        int height,
        int bitsPerComponent,
        int highValue,
        byte[] rgbPalette,
        double decodeMinimum,
        double decodeMaximum)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException(
                "PDF Indexed image имеет недопустимый размер.");
        }

        if (bitsPerComponent is not 1 and not 2 and not 4 and not 8)
        {
            throw new InvalidDataException(
                $"PDF Indexed image использует неподдерживаемые {bitsPerComponent} bits per component.");
        }

        if (highValue is < 0 or > 255)
        {
            throw new InvalidDataException(
                "PDF Indexed hival должен быть в диапазоне 0..255.");
        }

        var paletteEntries = checked(highValue + 1);

        if (rgbPalette.Length < paletteEntries * 3)
        {
            throw new InvalidDataException(
                "PDF Indexed lookup короче заявленного hival.");
        }

        var rowBytes = checked(
            (width * bitsPerComponent + 7) / 8);
        var expectedLength = checked(rowBytes * height);

        if (samples.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"PDF Indexed raster содержит {samples.Length} bytes вместо ожидаемых {expectedLength}.");
        }

        var output = new byte[checked(width * height * 3)];
        var mask = (1 << bitsPerComponent) - 1;

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * rowBytes;

            for (var x = 0; x < width; x++)
            {
                var bitOffset = x * bitsPerComponent;
                var sourceByte =
                    samples[rowStart + bitOffset / 8];
                var shift =
                    8 -
                    bitsPerComponent -
                    bitOffset % 8;
                var rawIndex =
                    (sourceByte >> shift) & mask;

                var mappedIndex =
                    decodeMinimum +
                    rawIndex / (double)mask *
                    (decodeMaximum - decodeMinimum);

                var paletteIndex = Math.Clamp(
                    (int)Math.Floor(mappedIndex + 0.5),
                    0,
                    highValue);

                var paletteOffset = paletteIndex * 3;
                var targetOffset = (y * width + x) * 3;

                output[targetOffset] =
                    rgbPalette[paletteOffset];
                output[targetOffset + 1] =
                    rgbPalette[paletteOffset + 1];
                output[targetOffset + 2] =
                    rgbPalette[paletteOffset + 2];
            }
        }

        return output;
    }

    /// <summary>
    /// Преобразует palette entries из DeviceGray/DeviceRGB/DeviceCMYK в RGB.
    /// </summary>
    public static byte[] ConvertPaletteToRgb(
        byte[] lookup,
        int entryCount,
        int baseComponents)
    {
        var expectedLength = checked(
            entryCount * baseComponents);

        if (lookup.Length < expectedLength)
        {
            throw new InvalidDataException(
                "PDF Indexed lookup не содержит все palette entries.");
        }

        if (baseComponents == 3)
        {
            return lookup
                .AsSpan(0, expectedLength)
                .ToArray();
        }

        var output = new byte[checked(entryCount * 3)];

        if (baseComponents == 1)
        {
            for (var index = 0; index < entryCount; index++)
            {
                var value = lookup[index];
                var target = index * 3;
                output[target] = value;
                output[target + 1] = value;
                output[target + 2] = value;
            }

            return output;
        }

        if (baseComponents == 4)
        {
            return ConvertCmykToRgb(
                lookup
                    .AsSpan(0, expectedLength)
                    .ToArray());
        }

        throw new InvalidDataException(
            "PDF Indexed base color space не поддерживается.");
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Round(
            Math.Clamp(value, 0, 1) * 255);
    }
}
