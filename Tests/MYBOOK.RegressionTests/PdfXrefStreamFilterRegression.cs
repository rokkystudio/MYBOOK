using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Book.Documents;
using MYBOOK.Services;

namespace MYBOOK.RegressionTests;

internal static partial class Program
{
    /// <summary>
    /// Проверяет lossless filter pipeline cross-reference streams:
    /// ASCIIHex/ASCII85/RunLength с abbreviations, filter arrays,
    /// Flate predictor и LZW DecodeParms. Malformed DecodeParms
    /// отклоняются до разбора document objects.
    /// </summary>
    private static void TestPdfXrefStreamFilters(string directory)
    {
        static byte[] EncodeAsciiHex(byte[] data)
        {
            return Encoding.ASCII.GetBytes(
                Convert.ToHexString(data) +
                ">");
        }

        static byte[] EncodeAscii85(byte[] data)
        {
            var output =
                new StringBuilder();
            var offset = 0;

            while (offset < data.Length)
            {
                var count =
                    Math.Min(
                        4,
                        data.Length - offset);

                uint value = 0;

                for (var index = 0;
                     index < 4;
                     index++)
                {
                    value <<= 8;

                    if (index < count)
                    {
                        value |= data[offset + index];
                    }
                }

                if (count == 4 &&
                    value == 0)
                {
                    output.Append('z');
                    offset += count;
                    continue;
                }

                var encoded =
                    new char[5];
                var current =
                    value;

                for (var index = 4;
                     index >= 0;
                     index--)
                {
                    encoded[index] =
                        (char)('!' + current % 85);
                    current /= 85;
                }

                var outputCount =
                    count == 4
                        ? 5
                        : count + 1;

                output.Append(
                    encoded,
                    0,
                    outputCount);
                offset += count;
            }

            output.Append("~>");

            return Encoding.ASCII.GetBytes(
                output.ToString());
        }

        static byte[] EncodeRunLength(byte[] data)
        {
            using var output =
                new MemoryStream();
            var offset = 0;

            while (offset < data.Length)
            {
                var count =
                    Math.Min(
                        128,
                        data.Length - offset);

                output.WriteByte(
                    checked((byte)(count - 1)));
                output.Write(
                    data,
                    offset,
                    count);
                offset += count;
            }

            output.WriteByte(128);
            return output.ToArray();
        }

        static byte[] EncodeFlatePredictor(
            byte[] data,
            int rowBytes)
        {
            if (data.Length % rowBytes != 0)
            {
                throw new InvalidOperationException(
                    "PDF xref predictor fixture имеет некратную длину строки.");
            }

            using var predicted =
                new MemoryStream();

            for (var offset = 0;
                 offset < data.Length;
                 offset += rowBytes)
            {
                predicted.WriteByte(0);
                predicted.Write(
                    data,
                    offset,
                    rowBytes);
            }

            using var compressed =
                new MemoryStream();

            using (var zlib =
                new ZLibStream(
                    compressed,
                    CompressionLevel.Optimal,
                    leaveOpen: true))
            {
                predicted.Position = 0;
                predicted.CopyTo(zlib);
            }

            return compressed.ToArray();
        }

        string ReadMarker(
            string suffix,
            string filterSyntax,
            string? decodeParmsSyntax,
            Func<byte[], byte[]> encoder)
        {
            var path =
                Path.Combine(
                    directory,
                    "xref-filter-" +
                    suffix +
                    ".pdf");

            CreateXrefFilterPdf(
                path,
                "XRef " + suffix,
                filterSyntax,
                decodeParmsSyntax,
                encoder);

            var model =
                SupportedFormatRegistry.ReadDocument(
                    path);
            var page =
                model.Blocks
                    .OfType<DocumentFixedPage>()
                    .Single();

            return page.Text;
        }

        bool Rejects(
            string suffix,
            string filterSyntax,
            string? decodeParmsSyntax,
            Func<byte[], byte[]> encoder,
            string diagnostic)
        {
            try
            {
                ReadMarker(
                    suffix,
                    filterSyntax,
                    decodeParmsSyntax,
                    encoder);
                return false;
            }
            catch (InvalidDataException error)
            {
                return error.Message.Contains(
                    diagnostic,
                    StringComparison.Ordinal);
            }
        }

        AssertContains(
            ReadMarker(
                "asciihex",
                "/ASCIIHexDecode",
                null,
                EncodeAsciiHex),
            "XRef asciihex",
            "PDF ASCIIHexDecode xref stream");

        AssertContains(
            ReadMarker(
                "ahx",
                "/AHx",
                null,
                EncodeAsciiHex),
            "XRef ahx",
            "PDF /AHx xref stream");

        AssertContains(
            ReadMarker(
                "ascii85",
                "/ASCII85Decode",
                null,
                EncodeAscii85),
            "XRef ascii85",
            "PDF ASCII85Decode xref stream");

        AssertContains(
            ReadMarker(
                "a85",
                "/A85",
                null,
                EncodeAscii85),
            "XRef a85",
            "PDF /A85 xref stream");

        AssertContains(
            ReadMarker(
                "runlength",
                "/RunLengthDecode",
                null,
                EncodeRunLength),
            "XRef runlength",
            "PDF RunLengthDecode xref stream");

        AssertContains(
            ReadMarker(
                "rl",
                "/RL",
                null,
                EncodeRunLength),
            "XRef rl",
            "PDF /RL xref stream");

        AssertContains(
            ReadMarker(
                "pipeline",
                "[/ASCII85Decode /RunLengthDecode]",
                "[null null]",
                data =>
                    EncodeAscii85(
                        EncodeRunLength(data))),
            "XRef pipeline",
            "PDF xref stream filter array decode order");

        AssertContains(
            ReadMarker(
                "flate-predictor",
                "/FlateDecode",
                "<< /Predictor 12 /Colors 1 /BitsPerComponent 8 /Columns 7 >>",
                data =>
                    EncodeFlatePredictor(
                        data,
                        7)),
            "XRef flate-predictor",
            "PDF xref FlateDecode predictor");

        AssertContains(
            ReadMarker(
                "lzw",
                "/LZWDecode",
                "<< /EarlyChange 1 >>",
                EncodeLzwLiteralStream),
            "XRef lzw",
            "PDF xref LZW DecodeParms");

        Assert(
            Rejects(
                "bad-asciihex-parms",
                "/ASCIIHexDecode",
                "<< /Predictor 1 >>",
                EncodeAsciiHex,
                "ASCIIHexDecode") &&
            Rejects(
                "bad-array-length",
                "[/ASCII85Decode /RunLengthDecode]",
                "[null]",
                data =>
                    EncodeAscii85(
                        EncodeRunLength(data)),
                "/DecodeParms array") &&
            Rejects(
                "bad-array-entry",
                "[/ASCII85Decode /RunLengthDecode]",
                "[null 7]",
                data =>
                    EncodeAscii85(
                        EncodeRunLength(data)),
                "не-dictionary") &&
            Rejects(
                "bad-lzw-earlychange",
                "/LZWDecode",
                "<< /EarlyChange 2 >>",
                EncodeLzwLiteralStream,
                "EarlyChange"),
            "PDF xref stream должен строго валидировать DecodeParms.");

        var noFilterPath =
            Path.Combine(
                directory,
                "xref-filter-no-filter-parms.pdf");

        CreateXrefFilterPdf(
            noFilterPath,
            "No filter",
            filterSyntax: null,
            decodeParmsSyntax: "<< /Predictor 1 >>",
            encoder: data => data);

        var rejectedWithoutFilter = false;

        try
        {
            _ = SupportedFormatRegistry.ReadDocument(
                noFilterPath);
        }
        catch (InvalidDataException error)
            when (error.Message.Contains(
                "/DecodeParms без /Filter",
                StringComparison.Ordinal))
        {
            rejectedWithoutFilter = true;
        }

        Assert(
            rejectedWithoutFilter,
            "PDF xref stream /DecodeParms без /Filter должен отклоняться.");
    }

    /// <summary>
    /// Создаёт минимальный PDF 1.5, в котором все indirect objects
    /// разрешаются только через cross-reference stream с заданным filter.
    /// </summary>
    private static void CreateXrefFilterPdf(
        string path,
        string marker,
        string? filterSyntax,
        string? decodeParmsSyntax,
        Func<byte[], byte[]> encoder)
    {
        var encoding =
            Encoding.ASCII;
        using var memory =
            new MemoryStream();
        var offsets =
            new long[7];

        void Write(string value)
        {
            var bytes =
                encoding.GetBytes(value);
            memory.Write(
                bytes,
                0,
                bytes.Length);
        }

        void WriteObject(
            int number,
            string body)
        {
            offsets[number] =
                memory.Position;
            Write(
                number.ToString(
                    CultureInfo.InvariantCulture) +
                " 0 obj\n" +
                body +
                "\nendobj\n");
        }

        Write("%PDF-1.5\n");

        WriteObject(
            1,
            "<< /Type /Catalog /Pages 2 0 R >>");

        WriteObject(
            2,
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 200] >>");

        WriteObject(
            3,
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");

        var content =
            encoding.GetBytes(
                "BT\n" +
                "/F1 12 Tf\n" +
                "1 0 0 1 20 100 Tm\n" +
                "(" +
                EscapePdfString(marker) +
                ") Tj\n" +
                "ET\n");

        offsets[4] =
            memory.Position;
        Write(
            "4 0 obj\n" +
            "<< /Length " +
            content.Length.ToString(
                CultureInfo.InvariantCulture) +
            " >>\n" +
            "stream\n");
        memory.Write(
            content,
            0,
            content.Length);
        Write(
            "\nendstream\n" +
            "endobj\n");

        WriteObject(
            5,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        offsets[6] =
            memory.Position;

        var xrefData =
            new byte[7 * 7];

        void WriteEntry(
            int objectNumber,
            byte type,
            long field2,
            int field3)
        {
            var offset =
                objectNumber * 7;

            xrefData[offset] =
                type;

            xrefData[offset + 1] =
                checked((byte)((field2 >> 24) & 0xFF));
            xrefData[offset + 2] =
                checked((byte)((field2 >> 16) & 0xFF));
            xrefData[offset + 3] =
                checked((byte)((field2 >> 8) & 0xFF));
            xrefData[offset + 4] =
                checked((byte)(field2 & 0xFF));
            xrefData[offset + 5] =
                checked((byte)((field3 >> 8) & 0xFF));
            xrefData[offset + 6] =
                checked((byte)(field3 & 0xFF));
        }

        WriteEntry(
            0,
            type: 0,
            field2: 0,
            field3: 65535);

        for (var number = 1;
             number <= 6;
             number++)
        {
            WriteEntry(
                number,
                type: 1,
                field2: offsets[number],
                field3: 0);
        }

        var encoded =
            encoder(xrefData);

        Write(
            "6 0 obj\n" +
            "<< /Type /XRef /Size 7 /Root 1 0 R /W [1 4 2] /Index [0 7] /Length " +
            encoded.Length.ToString(
                CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(filterSyntax))
        {
            Write(
                " /Filter " +
                filterSyntax);
        }

        if (!string.IsNullOrWhiteSpace(decodeParmsSyntax))
        {
            Write(
                " /DecodeParms " +
                decodeParmsSyntax);
        }

        Write(
            " >>\n" +
            "stream\n");

        memory.Write(
            encoded,
            0,
            encoded.Length);

        Write(
            "\nendstream\n" +
            "endobj\n" +
            "startxref\n" +
            offsets[6].ToString(
                CultureInfo.InvariantCulture) +
            "\n%%EOF\n");

        File.WriteAllBytes(
            path,
            memory.ToArray());
    }
}
