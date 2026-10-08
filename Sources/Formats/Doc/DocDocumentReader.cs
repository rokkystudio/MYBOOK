using System.Buffers.Binary;
using System.IO;
using System.Text;
using MYBOOK.Documents;
using MYBOOK.Services;

namespace MYBOOK.Formats.Doc;

/// <summary>
/// Читает классический Microsoft Word Binary File Format (.doc)
/// поверх собственного CFB-reader и извлекает основной текст через CLX piece table.
/// </summary>
internal static class DocDocumentReader
{
    private const ushort WordIdent = 0xA5EC;
    private const int FcLcbClxIndex = 33;

    /// <summary>
    /// Загружает основной текст старого DOC-файла и преобразует его
    /// в нейтральную модель MYBOOK.
    /// </summary>
    public static DocumentModel Read(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ParserTrace.Write(
            "doc",
            $"open path={Path.GetFileName(path)}");

        var compound = CompoundBinaryFile.Open(path);
        var word = compound.ReadStream("WordDocument");

        ParserTrace.Write(
            "doc",
            $"WordDocument-bytes={word.Length}");

        if (word.Length < 0x22)
        {
            throw new InvalidDataException("Поток WordDocument слишком короткий.");
        }

        var ident = ReadUInt16(word, 0x00);
        if (ident != WordIdent)
        {
            throw new InvalidDataException(
                $"WordDocument имеет неизвестный идентификатор 0x{ident:X4}.");
        }

        var flags = ReadUInt16(word, 0x0A);

        if ((flags & 0x0100) != 0)
        {
            throw new InvalidDataException("Зашифрованные DOC-файлы пока не поддерживаются.");
        }

        var useOneTable = (flags & 0x0200) != 0;
        var tableStreamName = useOneTable ? "1Table" : "0Table";

        if (!compound.ContainsStream(tableStreamName))
        {
            throw new InvalidDataException(
                $"DOC требует поток {tableStreamName}, но он отсутствует.");
        }

        var table = compound.ReadStream(tableStreamName);

        ParserTrace.Write(
            "doc",
            $"table-stream={tableStreamName} bytes={table.Length}");

        var fib = ReadFib(word);

        ParserTrace.Write(
            "doc",
            $"fib language=0x{fib.LanguageId:X4} ccpText={fib.CcpText} fcClx={fib.FcClx} lcbClx={fib.LcbClx}");

        if (fib.CcpText <= 0)
        {
            return new DocumentModel
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Blocks = Array.Empty<DocumentBlock>()
            };
        }

        if (fib.FcClx < 0 || fib.LcbClx <= 0 ||
            (long)fib.FcClx + fib.LcbClx > table.Length)
        {
            throw new InvalidDataException("DOC содержит недопустимый диапазон CLX.");
        }

        var pieces = ReadPieceTable(
            table.AsSpan(fib.FcClx, fib.LcbClx));

        ParserTrace.Write(
            "doc",
            $"pieces={pieces.Count}");

        var encoding = GetAnsiEncoding(fib.LanguageId);

        ParserTrace.Write(
            "doc",
            $"encoding={encoding.WebName}");
        var text = ReadMainText(
            word,
            pieces,
            fib.CcpText,
            encoding);

        var blocks = BuildParagraphs(text);

        ParserTrace.Write(
            "doc",
            $"text-chars={text.Length} blocks={blocks.Count}");

        return new DocumentModel
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Blocks = blocks
        };
    }

    private static FibInfo ReadFib(byte[] word)
    {
        var languageId = ReadUInt16(word, 0x06);

        var offset = 0x20;
        var csw = ReadUInt16(word, offset);
        offset += 2 + csw * 2;

        var cslw = ReadUInt16(word, offset);
        offset += 2;

        if (cslw < 4)
        {
            throw new InvalidDataException("FIB не содержит ccpText.");
        }

        var fibRgLwOffset = offset;
        var ccpText = ReadInt32(word, fibRgLwOffset + 3 * 4);

        offset += cslw * 4;

        var cbRgFcLcb = ReadUInt16(word, offset);
        offset += 2;

        if (cbRgFcLcb <= FcLcbClxIndex)
        {
            throw new InvalidDataException("FIB не содержит fcClx/lcbClx.");
        }

        var clxPairOffset = checked(offset + FcLcbClxIndex * 8);
        var fcClx = ReadInt32(word, clxPairOffset);
        var lcbClx = ReadInt32(word, clxPairOffset + 4);

        return new FibInfo(
            languageId,
            ccpText,
            fcClx,
            lcbClx);
    }

    private static IReadOnlyList<TextPiece> ReadPieceTable(ReadOnlySpan<byte> clx)
    {
        var offset = 0;

        while (offset < clx.Length)
        {
            var marker = clx[offset++];

            if (marker == 0x01)
            {
                if (offset + 2 > clx.Length)
                {
                    throw new InvalidDataException("CLX grpprl повреждён.");
                }

                var cbGrpprl = BinaryPrimitives.ReadUInt16LittleEndian(
                    clx.Slice(offset, 2));

                offset += 2 + cbGrpprl;
                continue;
            }

            if (marker != 0x02)
            {
                throw new InvalidDataException(
                    $"CLX содержит неизвестный marker 0x{marker:X2}.");
            }

            if (offset + 4 > clx.Length)
            {
                throw new InvalidDataException("CLX Pcdt не содержит lcb.");
            }

            var lcb = BinaryPrimitives.ReadInt32LittleEndian(
                clx.Slice(offset, 4));
            offset += 4;

            if (lcb < 4 || offset + lcb > clx.Length || (lcb - 4) % 12 != 0)
            {
                throw new InvalidDataException("DOC содержит повреждённую PlcPcd.");
            }

            var pieceCount = (lcb - 4) / 12;
            var plc = clx.Slice(offset, lcb);
            var cpBytes = checked((pieceCount + 1) * 4);
            var pcdOffset = cpBytes;

            var result = new List<TextPiece>(pieceCount);

            for (var index = 0; index < pieceCount; index++)
            {
                var cpStart = BinaryPrimitives.ReadInt32LittleEndian(
                    plc.Slice(index * 4, 4));
                var cpEnd = BinaryPrimitives.ReadInt32LittleEndian(
                    plc.Slice((index + 1) * 4, 4));

                var pcd = plc.Slice(pcdOffset + index * 8, 8);
                var fcCompressed = BinaryPrimitives.ReadUInt32LittleEndian(
                    pcd.Slice(2, 4));

                var compressed = (fcCompressed & 0x40000000) != 0;
                var fc = checked((int)(fcCompressed & 0x3FFFFFFF));

                if (compressed)
                {
                    fc /= 2;
                }

                if (cpEnd < cpStart)
                {
                    throw new InvalidDataException("DOC piece table содержит обратный CP-диапазон.");
                }

                result.Add(new TextPiece(
                    cpStart,
                    cpEnd,
                    fc,
                    compressed));
            }

            return result;
        }

        throw new InvalidDataException("CLX не содержит Pcdt.");
    }

    private static string ReadMainText(
        byte[] word,
        IReadOnlyList<TextPiece> pieces,
        int ccpText,
        Encoding ansiEncoding)
    {
        var output = new StringBuilder(Math.Max(ccpText, 16));
        var remaining = ccpText;

        foreach (var piece in pieces)
        {
            if (remaining <= 0)
            {
                break;
            }

            var characters = piece.CpEnd - piece.CpStart;
            if (characters <= 0)
            {
                continue;
            }

            characters = Math.Min(characters, remaining);

            if (piece.Compressed)
            {
                EnsureRange(word, piece.FileOffset, characters);
                output.Append(
                    ansiEncoding.GetString(
                        word,
                        piece.FileOffset,
                        characters));
            }
            else
            {
                var byteCount = checked(characters * 2);
                EnsureRange(word, piece.FileOffset, byteCount);
                output.Append(
                    Encoding.Unicode.GetString(
                        word,
                        piece.FileOffset,
                        byteCount));
            }

            remaining -= characters;
        }

        if (remaining > 0)
        {
            throw new InvalidDataException(
                "Piece table закончилась до окончания основного текста DOC.");
        }

        return output.ToString();
    }

    private static IReadOnlyList<DocumentBlock> BuildParagraphs(string text)
    {
        var blocks = new List<DocumentBlock>();
        var paragraph = new StringBuilder();

        void Flush()
        {
            var value = paragraph.ToString();
            paragraph.Clear();

            if (value.Length == 0)
            {
                return;
            }

            blocks.Add(new DocumentParagraph
            {
                Inlines = new[]
                {
                    new DocumentInline
                    {
                        Text = value
                    }
                }
            });
        }

        foreach (var character in text)
        {
            switch (character)
            {
                case '\r':
                case '\u0007':
                    Flush();
                    break;

                case '\u000B':
                    paragraph.Append(Environment.NewLine);
                    break;

                case '\u000C':
                    Flush();
                    break;

                case '\u0013':
                case '\u0014':
                case '\u0015':
                case '\u0001':
                    break;

                default:
                    if (!char.IsControl(character) || character == '\t')
                    {
                        paragraph.Append(character);
                    }
                    break;
            }
        }

        Flush();
        return blocks;
    }

    /// <summary>
    /// Возвращает ANSI-кодировку сжатых DOC-piece по языковому идентификатору FIB.
    /// Для языков без однозначного поддерживаемого сопоставления выдаёт явную ошибку.
    /// </summary>
    private static Encoding GetAnsiEncoding(ushort languageId)
    {
        var codePage = languageId switch
        {
            0x0404 or 0x0C04 or 0x1404 => 950,
            0x0804 or 0x1004 => 936,
            _ => (languageId & 0x03FF) switch
            {
                0x01 => 1256,
                0x02 => 1251,
                0x03 => 1252,
                0x05 => 1250,
                0x06 => 1252,
                0x07 => 1252,
                0x08 => 1253,
                0x09 => 1252,
                0x0A => 1252,
                0x0B => 1252,
                0x0C => 1252,
                0x0D => 1255,
                0x0E => 1250,
                0x0F => 1252,
                0x10 => 1252,
                0x11 => 932,
                0x12 => 949,
                0x13 => 1252,
                0x14 => 1252,
                0x15 => 1250,
                0x16 => 1252,
                0x18 => 1250,
                0x19 => 1251,
                0x1A => 1250,
                0x1B => 1250,
                0x1C => 1250,
                0x1D => 1252,
                0x1E => 874,
                0x1F => 1254,
                0x20 => 1256,
                0x21 => 1252,
                0x22 => 1251,
                0x23 => 1251,
                0x24 => 1250,
                0x25 => 1257,
                0x26 => 1257,
                0x27 => 1257,
                0x29 => 1256,
                0x2A => 1258,
                _ => throw new InvalidDataException(
                    $"DOC использует неподдерживаемый языковой идентификатор 0x{languageId:X4}.")
            }
        };

        return Encoding.GetEncoding(
            codePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    private static void EnsureRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || (long)offset + length > data.Length)
        {
            throw new InvalidDataException("DOC piece выходит за границы WordDocument.");
        }
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        EnsureRange(data, offset, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    }

    private static int ReadInt32(byte[] data, int offset)
    {
        EnsureRange(data, offset, 4);
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private sealed record FibInfo(
        ushort LanguageId,
        int CcpText,
        int FcClx,
        int LcbClx);

    private sealed record TextPiece(
        int CpStart,
        int CpEnd,
        int FileOffset,
        bool Compressed);
}
