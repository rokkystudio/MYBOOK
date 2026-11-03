using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Book.Documents;
using Book.Formats.Pdf;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет TIFF Predictor 2 и PNG predictors 10..15 на всех допустимых
    /// глубинах компонентов, строгие границы строк и интеграцию FlateDecode
    /// с двумя Image XObjects в PDF reader.
    /// </summary>
    private static void TestPdfPredictors(string directory)
    {
        static void AssertDecoded(
            byte[] encoded,
            byte[] expected,
            int predictor,
            int colors,
            int bits,
            int columns,
            string label)
        {
            var actual = PdfPredictorDecoder.Decode(
                encoded,
                predictor,
                colors,
                bits,
                columns);

            Assert(
                actual.AsSpan().SequenceEqual(expected),
                $"PDF predictor {predictor} {label}: ожидались {Convert.ToHexString(expected)}, получены {Convert.ToHexString(actual)}.");
        }

        static bool RejectsPredictor(
            byte[] encoded,
            int predictor,
            int colors,
            int bits,
            int columns)
        {
            try
            {
                PdfPredictorDecoder.Decode(
                    encoded,
                    predictor,
                    colors,
                    bits,
                    columns);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        AssertDecoded(
            [0xB3],
            [0xD3],
            2,
            1,
            1,
            3,
            "1-bit values modulo 2, trailing padding preserved");

        AssertDecoded(
            [0x6D, 0x50],
            [0x6E, 0xC0],
            2,
            3,
            2,
            2,
            "2-bit components reference previous pixel of the same colour");

        AssertDecoded(
            [0xF2, 0xE7],
            [0xF1, 0xF6],
            2,
            1,
            4,
            4,
            "4-bit modulo 16");

        AssertDecoded(
            [250, 100, 10, 200, 7, 9],
            [250, 100, 4, 44, 11, 53],
            2,
            2,
            8,
            3,
            "8-bit multi-component row");

        AssertDecoded(
            [250, 100, 10, 200, 7, 9, 250, 100, 10, 200, 7, 9],
            [250, 100, 4, 44, 11, 53, 250, 100, 4, 44, 11, 53],
            2,
            2,
            8,
            3,
            "TIFF predictor resets at each row boundary");

        AssertDecoded(
            [0xFF, 0xFE, 0x00, 0x01, 0x00, 0x03, 0xFF, 0xFF],
            [0xFF, 0xFE, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00],
            2,
            2,
            16,
            2,
            "16-bit big-endian modulo 65536");

        for (var predictor = 10;
             predictor <= 15;
             predictor++)
        {
            AssertDecoded(
                [1, 10, 10, 2, 5, 1],
                [10, 20, 15, 21],
                predictor,
                1,
                8,
                2,
                "per-row filter bytes independent of global Predictor");
        }

        AssertDecoded(
            [1, 0xA1, 0xDF, 2, 0xE1, 0x80],
            [0xA1, 0x80, 0x82, 0x00],
            15,
            1,
            1,
            9,
            "PNG 1-bit packed Sub and Up with padded rows");

        AssertDecoded(
            [0, 0x21, 0x43, 0x80, 4, 0x14, 0x24, 0x40],
            [0x21, 0x43, 0x80, 0x35, 0x67, 0xC0],
            14,
            3,
            2,
            3,
            "PNG 2-bit multi-component Paeth");

        AssertDecoded(
            [0, 0x12, 0x30, 3, 0x1B, 0x26],
            [0x12, 0x30, 0x24, 0x50],
            13,
            1,
            4,
            3,
            "PNG 4-bit Average");

        AssertDecoded(
            [1, 0x12, 0x34, 0x44, 0x44],
            [0x12, 0x34, 0x56, 0x78],
            11,
            1,
            16,
            2,
            "PNG 16-bit big-endian byte filtering");

        AssertDecoded(
            [0, 0x11, 0x22, 0x33, 0x44],
            [0x11, 0x22, 0x33, 0x44],
            15,
            1,
            8,
            4,
            "PNG None filter");

        Assert(
            RejectsPredictor([1, 2], 3, 1, 8, 2) &&
            RejectsPredictor([1], 2, 1, 3, 1) &&
            RejectsPredictor([1], 2, 0, 8, 1) &&
            RejectsPredictor([1], 2, 1, 16, 0) &&
            RejectsPredictor([1], 2, 1, 8, 2) &&
            RejectsPredictor([1, 10], 10, 1, 8, 2) &&
            RejectsPredictor([5, 10, 20], 15, 1, 8, 2) &&
            RejectsPredictor([0, 10, 20, 2, 30], 12, 1, 8, 2) &&
            RejectsPredictor([0], 15, int.MaxValue, 16, int.MaxValue),
            "PDF predictor rejects unsupported modes, invalid parameters, malformed row geometry and filter tags.");

        var pdfPath = Path.Combine(
            directory,
            "pdf-flate-packed-predictors.pdf");

        CreatePredictorPdf(pdfPath);

        var model =
            PdfDocumentReader.Read(pdfPath);
        var page =
            model.Blocks
                .OfType<DocumentFixedPage>()
                .Single();

        Assert(
            page.ImageRuns.Count == 2,
            "PDF reader должен отрисовать PNG и TIFF FlateDecode Image XObjects.");

        foreach (var image in page.ImageRuns)
        {
            Assert(
                image.ContentType == "image/png",
                "PDF predictor Image XObject должен выводиться в lossless PNG.");

            using var stream =
                new MemoryStream(image.Data);
            var decoder =
                new PngBitmapDecoder(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
            var bitmap =
                new FormatConvertedBitmap(
                    decoder.Frames.Single(),
                    PixelFormats.Bgra32,
                    null,
                    0);
            var pixels =
                new byte[16];
            bitmap.CopyPixels(
                pixels,
                8,
                0);

            Assert(
                bitmap.PixelWidth == 2 &&
                bitmap.PixelHeight == 2 &&
                pixels.AsSpan().SequenceEqual(
                    new byte[]
                    {
                        10, 10, 10, 255,
                        20, 20, 20, 255,
                        15, 15, 15, 255,
                        21, 21, 21, 255
                    }),
                "PDF reader FlateDecode Image XObject должен применять predictor до построения raster.");
        }
    }

    /// <summary>
    /// Создаёт минимальный PDF с двумя FlateDecode DeviceGray Image XObjects:
    /// PNG per-row Sub/Up tags при Predictor 12 и TIFF Predictor 2.
    /// </summary>
    private static void CreatePredictorPdf(string path)
    {
        static byte[] Compress(ReadOnlySpan<byte> source)
        {
            using var output =
                new MemoryStream();

            using (var zlib =
                new ZLibStream(
                    output,
                    CompressionLevel.Optimal,
                    leaveOpen: true))
            {
                zlib.Write(source);
            }

            return output.ToArray();
        }

        var encoding =
            Encoding.ASCII;
        var content =
            encoding.GetBytes(
                "q 20 0 0 20 10 10 cm /ImP Do Q\n" +
                "q 20 0 0 20 40 10 cm /ImT Do Q\n");
        var pngEncoded =
            Compress([1, 10, 10, 2, 5, 1]);
        var tiffEncoded =
            Compress([10, 10, 15, 6]);

        using var memory =
            new MemoryStream();
        var offsets =
            new List<long>();

        void Write(string value)
        {
            var bytes =
                encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        void BeginObject(int number)
        {
            offsets.Add(memory.Position);
            Write(
                number.ToString(CultureInfo.InvariantCulture) +
                " 0 obj\n");
        }

        Write("%PDF-1.5\n");

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        Write("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>\nendobj\n");

        BeginObject(3);
        Write("<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /XObject << /ImP 5 0 R /ImT 6 0 R >> >> >>\nendobj\n");

        BeginObject(4);
        Write(
            $"<< /Length {content.Length} >>\nstream\n");
        memory.Write(
            content,
            0,
            content.Length);
        Write("\nendstream\nendobj\n");

        BeginObject(5);
        Write(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
            "/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode " +
            "/DecodeParms << /Predictor 12 /Colors 1 /BitsPerComponent 8 /Columns 2 >> " +
            $"/Length {pngEncoded.Length} >>\nstream\n");
        memory.Write(
            pngEncoded,
            0,
            pngEncoded.Length);
        Write("\nendstream\nendobj\n");

        BeginObject(6);
        Write(
            "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
            "/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode " +
            "/DecodeParms << /Predictor 2 /Colors 1 /BitsPerComponent 8 /Columns 2 >> " +
            $"/Length {tiffEncoded.Length} >>\nstream\n");
        memory.Write(
            tiffEncoded,
            0,
            tiffEncoded.Length);
        Write("\nendstream\nendobj\n");

        var xref =
            memory.Position;
        Write("xref\n0 7\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            Write(
                offset.ToString(
                    "0000000000",
                    CultureInfo.InvariantCulture) +
                " 00000 n \n");
        }

        Write(
            "trailer\n" +
            "<< /Size 7 /Root 1 0 R >>\n" +
            "startxref\n" +
            xref.ToString(CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }
}
