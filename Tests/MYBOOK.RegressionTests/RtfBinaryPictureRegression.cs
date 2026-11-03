using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using Book.Documents;
using Book.Formats.Rtf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет RTF picture payload из \binN: точные raw bytes,
    /// включая RTF delimiters, высокие байты и CR/LF; изображение
    /// и размеры в модели, пропуск binary в ignorable destinations,
    /// а также malformed length и смешение binary/hex representations.
    /// </summary>
    private static void TestRtfBinaryPictures(string directory)
    {
        var path = Path.Combine(
            directory,
            "rtf-binary-picture.rtf");

        DocumentModel Read(ReadOnlySpan<byte> bytes)
        {
            File.WriteAllBytes(
                path,
                bytes.ToArray());
            return RtfDocumentReader.Read(path);
        }

        DocumentModel ReadText(string source)
        {
            return Read(
                Encoding.Latin1.GetBytes(source));
        }

        bool RejectsInvalid(string source)
        {
            try
            {
                ReadText(source);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        var png =
            CreateRtfBinaryPicturePng();

        Assert(
            png.Contains((byte)'{') &&
            png.Contains((byte)'}') &&
            png.Contains((byte)'\\') &&
            png.Contains((byte)'\r') &&
            png.Contains((byte)'\n') &&
            png.Contains((byte)0) &&
            png.Contains((byte)0xFF),
            "RTF binary picture fixture должен содержать все RTF delimiters и raw bytes.");

        var prefix =
            Encoding.Latin1.GetBytes(
                @"{\rtf1\ansi Before {\pict\pngblip\picw1\pich1\picwgoal1440\pichgoal720\bin" +
                png.Length.ToString(CultureInfo.InvariantCulture) +
                " ");
        var suffix =
            Encoding.Latin1.GetBytes(
                @"} after\par}");

        var imageFile =
            new byte[prefix.Length + png.Length + suffix.Length];

        prefix.CopyTo(
            imageFile,
            0);
        png.CopyTo(
            imageFile,
            prefix.Length);
        suffix.CopyTo(
            imageFile,
            prefix.Length + png.Length);

        var model =
            Read(imageFile);
        var paragraphs =
            model.Blocks
                .OfType<DocumentParagraph>()
                .ToArray();
        var images =
            paragraphs.SelectMany(item => item.Inlines)
                .Where(inline => inline.Image != null)
                .ToArray();

        Assert(
            images.Length == 1,
            "RTF binary picture должен содержать один image inline; actual paragraphs=" +
            paragraphs.Length + ", image inlines=" + images.Length +
            ", text=" + FlattenText(model));

        var image =
            images[0].Image!;

        Assert(
            image.Data.AsSpan().SequenceEqual(png) &&
            image.ContentType == "image/png" &&
            image.PixelWidth == 1 &&
            image.PixelHeight == 1 &&
            image.WidthPoints == 72 &&
            image.HeightPoints == 36,
            "RTF \\binN должен сохранять PNG bytes и исходную picture geometry.");

        var text =
            FlattenText(model);

        Assert(
            text.Contains("Before", StringComparison.Ordinal) &&
            text.Contains("after", StringComparison.Ordinal) &&
            !text.Contains("Note", StringComparison.Ordinal),
            "RTF \\binN raw bytes не должны попадать в видимый текст.");

        using (var stream = new MemoryStream(image.Data))
        {
            var decoder =
                new PngBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

            Assert(
                decoder.Frames.Count == 1 &&
                decoder.Frames[0].PixelWidth == 1 &&
                decoder.Frames[0].PixelHeight == 1,
                "RTF binary picture должен быть корректным PNG для bitmap decoder.");
        }

        var hexModel = ReadText(
            @"{\rtf1\ansi HexBefore {\pict\pngblip " +
            Convert.ToHexString(png) +
            @"} HexAfter\par}");

        var hexImages =
            hexModel.Blocks.OfType<DocumentParagraph>()
                .SelectMany(item => item.Inlines)
                .Where(inline => inline.Image != null)
                .ToArray();

        Assert(
            hexImages.Length == 1 &&
            hexImages[0].Image!.Data.AsSpan().SequenceEqual(png) &&
            FlattenText(hexModel).Contains("HexBefore", StringComparison.Ordinal) &&
            FlattenText(hexModel).Contains("HexAfter", StringComparison.Ordinal),
            "RTF hex picture должен сохраняться при добавлении последующего текстового run.");

        var skipBytes =
            Encoding.Latin1.GetBytes(
                @"{\rtf1\ansi First {\*\objdata\bin7 ");
        var skipRaw =
            new byte[]
            {
                (byte)'{',
                (byte)'}',
                (byte)'\\',
                (byte)'\r',
                (byte)'\n',
                0,
                0xFF
            };
        var skipSuffix =
            Encoding.Latin1.GetBytes(
                @"} Second\par}");
        var skipFile =
            skipBytes.Concat(skipRaw).Concat(skipSuffix).ToArray();
        var ignored =
            Read(skipFile);

        Assert(
            FlattenText(ignored).Contains("First", StringComparison.Ordinal) &&
            FlattenText(ignored).Contains("Second", StringComparison.Ordinal) &&
            ignored.Blocks.OfType<DocumentParagraph>().Count() == 1,
            "RTF \\binN внутри ignorable destination должен пропустить raw bytes без изменения структуры.");

        Assert(
            RejectsInvalid(@"{\rtf1{\pict\pngblip\bin XYZ}}") &&
            RejectsInvalid(@"{\rtf1{\pict\pngblip\bin-1 XYZ}}") &&
            RejectsInvalid(@"{\rtf1{\pict\pngblip\bin2147483648 XYZ}}") &&
            RejectsInvalid(@"{\rtf1{\pict\pngblip\bin100 ABC}}") &&
            RejectsInvalid(@"{\rtf1{\pict\pngblip DEADBEEF\bin2 XY}}") &&
            RejectsInvalid(@"{\rtf1{\pict\pngblip\bin2 XY DEADBEEF}}") &&
            RejectsInvalid(@"{\rtf1{\pict\pngblip\bin2 XY\bin2 XY}}") &&
            RejectsInvalid(@"{\rtf1{\*\objdata\bin100 ABC}}"),
            "RTF binN должен отклонять отсутствующую/отрицательную/overflow/short длину и неоднозначный picture encoding.");

        var nonPictureUnsupported = false;

        try
        {
            ReadText(@"{\rtf1 Normal \bin2 XY\par}");
        }
        catch (NotSupportedException)
        {
            nonPictureUnsupported = true;
        }

        Assert(
            nonPictureUnsupported,
            "RTF binN вне pict/ignorable destination должен давать explicit unsupported.");

        var empty =
            ReadText(@"{\rtf1{\pict\pngblip\bin0 }Empty\par}");

        Assert(
            !empty.Blocks.OfType<DocumentParagraph>()
                .SelectMany(item => item.Inlines)
                .Any(inline => inline.Image != null) &&
            FlattenText(empty).Contains("Empty", StringComparison.Ordinal),
            "RTF bin0 должен представлять пустой binary payload без фиктивного изображения.");
    }

    /// <summary>
    /// Создаёт PNG 1x1 с корректным ancillary tEXt chunk, содержащим
    /// нуль, фигурные скобки, backslash, CR/LF и 8-bit Latin-1 байт.
    /// CRC32 вычисляется по PNG chunk type и payload.
    /// </summary>
    private static byte[] CreateRtfBinaryPicturePng()
    {
        var baseImage =
            Convert.FromHexString(
                "89504E470D0A1A0A0000000D4948445200000001000000010804000000B51C0C02" +
                "0000000B4944415478DA63FCFF1F0002EB01F56950DA2A0000000049454E44AE426082");

        var type =
            Encoding.ASCII.GetBytes("tEXt");
        var comment =
            Encoding.Latin1.GetBytes("Note\0{\\}\r\n\u00FF");

        using var stream =
            new MemoryStream();

        stream.Write(
            baseImage.AsSpan(0, baseImage.Length - 12));

        Span<byte> number =
            stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(
            number,
            comment.Length);
        stream.Write(number);
        stream.Write(type);
        stream.Write(comment);

        var crc = 0xFFFFFFFFu;

        foreach (var value in type.Concat(comment))
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0
                    ? (crc >> 1) ^ 0xEDB88320u
                    : crc >> 1;
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(
            number,
            ~crc);
        stream.Write(number);

        stream.Write(
            baseImage.AsSpan(baseImage.Length - 12));

        return stream.ToArray();
    }
}
