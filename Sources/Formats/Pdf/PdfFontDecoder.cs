using System.Globalization;
using System.IO;
using System.Text;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Декодирует коды PDF-шрифта в Unicode.
/// Поддерживает ToUnicode CMap с codespacerange, bfchar и bfrange.
/// </summary>
internal sealed class PdfFontDecoder
{
    private readonly IReadOnlyDictionary<string, string> mappings_;
    private readonly int[] codeLengths_;

    private PdfFontDecoder(
        IReadOnlyDictionary<string, string> mappings,
        IEnumerable<int> codeLengths)
    {
        mappings_ = mappings;
        codeLengths_ = codeLengths
            .Where(length => length > 0)
            .Distinct()
            .OrderByDescending(length => length)
            .ToArray();

        if (codeLengths_.Length == 0)
        {
            codeLengths_ = mappings.Keys
                .Select(key => key.Length / 2)
                .Where(length => length > 0)
                .Distinct()
                .OrderByDescending(length => length)
                .ToArray();
        }
    }

    /// <summary>
    /// Создаёт decoder из декодированного содержимого ToUnicode CMap.
    /// </summary>
    public static PdfFontDecoder FromToUnicode(byte[] cmapData)
    {
        var parser = new CMapParser(cmapData);
        return parser.Parse();
    }

    /// <summary>
    /// Декодирует последовательность character codes активного PDF-шрифта.
    /// Неизвестные коды обозначаются символом замены Unicode.
    /// </summary>
    public string Decode(byte[] data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var output = new StringBuilder();
        var position = 0;

        while (position < data.Length)
        {
            var matched = false;

            foreach (var length in codeLengths_)
            {
                if (position + length > data.Length)
                {
                    continue;
                }

                var key = Convert.ToHexString(
                    data,
                    position,
                    length);

                if (!mappings_.TryGetValue(key, out var text))
                {
                    continue;
                }

                output.Append(text);
                position += length;
                matched = true;
                break;
            }

            if (matched)
            {
                continue;
            }

            output.Append('\uFFFD');
            position += GetFallbackCodeLength(data.Length - position);
        }

        return output.ToString();
    }

    /// <summary>
    /// Декодирует PDF-string без ToUnicode CMap.
    /// UTF-16BE BOM обрабатывается явно; остальные байты сохраняются как Latin-1.
    /// </summary>
    public static string DecodeFallback(byte[] data)
    {
        if (data.Length >= 2 &&
            data[0] == 0xFE &&
            data[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(
                data,
                2,
                data.Length - 2);
        }

        return Encoding.Latin1.GetString(data);
    }

    private int GetFallbackCodeLength(int remaining)
    {
        if (codeLengths_.Length == 0)
        {
            return 1;
        }

        foreach (var length in codeLengths_.OrderBy(length => length))
        {
            if (length <= remaining)
            {
                return length;
            }
        }

        return 1;
    }

    private sealed class CMapParser
    {
        private readonly CMapLexer lexer_;
        private readonly Dictionary<string, string> mappings_ =
            new(StringComparer.Ordinal);
        private readonly HashSet<int> codeLengths_ = new();

        public CMapParser(byte[] data)
        {
            lexer_ = new CMapLexer(data);
        }

        public PdfFontDecoder Parse()
        {
            CMapToken? previous = null;

            while (lexer_.Read() is { } token)
            {
                if (token.Kind != CMapTokenKind.Word)
                {
                    previous = token;
                    continue;
                }

                switch (token.Value)
                {
                    case "begincodespacerange":
                        ParseCodeSpaceRange(ReadCount(previous));
                        break;

                    case "beginbfchar":
                        ParseBfChar(ReadCount(previous));
                        break;

                    case "beginbfrange":
                        ParseBfRange(ReadCount(previous));
                        break;
                }

                previous = token;
            }

            if (mappings_.Count == 0)
            {
                throw new InvalidDataException(
                    "PDF ToUnicode CMap не содержит bfchar/bfrange mappings.");
            }

            return new PdfFontDecoder(
                mappings_,
                codeLengths_);
        }

        private void ParseCodeSpaceRange(int count)
        {
            for (var index = 0; index < count; index++)
            {
                var start = ReadHex();
                var end = ReadHex();

                if (start.Length != end.Length || start.Length == 0)
                {
                    throw new InvalidDataException(
                        "PDF ToUnicode codespacerange содержит несовместимые границы.");
                }

                codeLengths_.Add(start.Length);
            }

            ExpectWord("endcodespacerange");
        }

        private void ParseBfChar(int count)
        {
            for (var index = 0; index < count; index++)
            {
                var source = ReadHex();
                var destination = ReadHex();

                codeLengths_.Add(source.Length);
                mappings_[Convert.ToHexString(source)] =
                    DecodeDestination(destination);
            }

            ExpectWord("endbfchar");
        }

        private void ParseBfRange(int count)
        {
            for (var index = 0; index < count; index++)
            {
                var start = ReadHex();
                var end = ReadHex();

                if (start.Length != end.Length || start.Length == 0)
                {
                    throw new InvalidDataException(
                        "PDF ToUnicode bfrange содержит несовместимые source-коды.");
                }

                codeLengths_.Add(start.Length);

                var startValue = ReadUnsignedBigEndian(start);
                var endValue = ReadUnsignedBigEndian(end);

                if (endValue < startValue)
                {
                    throw new InvalidDataException(
                        "PDF ToUnicode bfrange имеет обратный диапазон.");
                }

                var destination = lexer_.Read()
                                  ?? throw new InvalidDataException(
                                      "PDF ToUnicode bfrange не содержит destination.");

                if (destination.Kind == CMapTokenKind.Hex)
                {
                    MapSequentialRange(
                        start.Length,
                        startValue,
                        endValue,
                        destination.Bytes!);
                    continue;
                }

                if (destination.Kind != CMapTokenKind.LeftBracket)
                {
                    throw new InvalidDataException(
                        "PDF ToUnicode bfrange destination имеет неподдерживаемый тип.");
                }

                MapArrayRange(
                    start.Length,
                    startValue,
                    endValue);
            }

            ExpectWord("endbfrange");
        }

        private void MapSequentialRange(
            int sourceLength,
            ulong start,
            ulong end,
            byte[] firstDestination)
        {
            var destination = firstDestination.ToArray();
            var source = start;

            while (true)
            {
                mappings_[FormatSource(source, sourceLength)] =
                    DecodeDestination(destination);

                if (source == end)
                {
                    break;
                }

                IncrementBigEndian(destination);
                source++;
            }
        }

        private void MapArrayRange(
            int sourceLength,
            ulong start,
            ulong end)
        {
            var source = start;

            while (true)
            {
                var destination = lexer_.Read()
                                  ?? throw new InvalidDataException(
                                      "PDF ToUnicode bfrange array завершился преждевременно.");

                if (destination.Kind != CMapTokenKind.Hex)
                {
                    throw new InvalidDataException(
                        "PDF ToUnicode bfrange array содержит не hex-string.");
                }

                mappings_[FormatSource(source, sourceLength)] =
                    DecodeDestination(destination.Bytes!);

                if (source == end)
                {
                    break;
                }

                source++;
            }

            var close = lexer_.Read();
            if (close?.Kind != CMapTokenKind.RightBracket)
            {
                throw new InvalidDataException(
                    "PDF ToUnicode bfrange array не закрыт.");
            }
        }

        private byte[] ReadHex()
        {
            var token = lexer_.Read()
                        ?? throw new InvalidDataException(
                            "Неожиданный конец PDF ToUnicode CMap.");

            if (token.Kind != CMapTokenKind.Hex)
            {
                throw new InvalidDataException(
                    "PDF ToUnicode CMap ожидал hex-string.");
            }

            return token.Bytes!;
        }

        private void ExpectWord(string value)
        {
            var token = lexer_.Read();

            if (token?.Kind != CMapTokenKind.Word ||
                !string.Equals(
                    token.Value,
                    value,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"PDF ToUnicode CMap не содержит ожидаемый {value}.");
            }
        }

        private static int ReadCount(CMapToken? token)
        {
            if (token?.Kind != CMapTokenKind.Word ||
                !int.TryParse(
                    token.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var count) ||
                count < 0)
            {
                throw new InvalidDataException(
                    "PDF ToUnicode CMap не содержит корректный count перед begin-оператором.");
            }

            return count;
        }

        private static string DecodeDestination(byte[] data)
        {
            if ((data.Length & 1) != 0)
            {
                throw new InvalidDataException(
                    "PDF ToUnicode destination должен содержать UTF-16BE code units.");
            }

            return Encoding.BigEndianUnicode.GetString(data);
        }

        private static ulong ReadUnsignedBigEndian(byte[] data)
        {
            if (data.Length > sizeof(ulong))
            {
                throw new InvalidDataException(
                    "PDF ToUnicode source code длиннее 8 байт.");
            }

            ulong value = 0;

            foreach (var item in data)
            {
                value = (value << 8) | item;
            }

            return value;
        }

        private static string FormatSource(
            ulong value,
            int byteLength)
        {
            if (byteLength > sizeof(ulong))
            {
                throw new InvalidDataException(
                    "PDF ToUnicode source code длиннее 8 байт.");
            }

            Span<byte> buffer = stackalloc byte[sizeof(ulong)];

            for (var index = byteLength - 1; index >= 0; index--)
            {
                buffer[index] = (byte)(value & 0xFF);
                value >>= 8;
            }

            return Convert.ToHexString(
                buffer[..byteLength]);
        }

        private static void IncrementBigEndian(byte[] value)
        {
            for (var index = value.Length - 1; index >= 0; index--)
            {
                value[index]++;

                if (value[index] != 0)
                {
                    return;
                }
            }

            throw new InvalidDataException(
                "PDF ToUnicode destination range переполнен.");
        }
    }

    private sealed class CMapLexer
    {
        private readonly byte[] data_;
        private int position_;

        public CMapLexer(byte[] data)
        {
            data_ = data;
        }

        public CMapToken? Read()
        {
            SkipWhiteSpaceAndComments();

            if (position_ >= data_.Length)
            {
                return null;
            }

            var value = data_[position_];

            if (value == (byte)'<')
            {
                return new CMapToken(
                    CMapTokenKind.Hex,
                    null,
                    ReadHex());
            }

            if (value == (byte)'[')
            {
                position_++;
                return new CMapToken(
                    CMapTokenKind.LeftBracket,
                    "[",
                    null);
            }

            if (value == (byte)']')
            {
                position_++;
                return new CMapToken(
                    CMapTokenKind.RightBracket,
                    "]",
                    null);
            }

            var start = position_;

            while (position_ < data_.Length &&
                   !IsWhiteSpace(data_[position_]) &&
                   data_[position_] is not (byte)'<' and
                       not (byte)'[' and
                       not (byte)']')
            {
                position_++;
            }

            if (position_ == start)
            {
                throw new InvalidDataException(
                    "PDF ToUnicode CMap содержит неизвестный токен.");
            }

            return new CMapToken(
                CMapTokenKind.Word,
                Encoding.ASCII.GetString(
                    data_,
                    start,
                    position_ - start),
                null);
        }

        private byte[] ReadHex()
        {
            position_++;
            var nibbles = new List<int>();

            while (position_ < data_.Length)
            {
                var value = data_[position_++];

                if (value == (byte)'>')
                {
                    if ((nibbles.Count & 1) != 0)
                    {
                        nibbles.Add(0);
                    }

                    var result = new byte[nibbles.Count / 2];

                    for (var index = 0; index < result.Length; index++)
                    {
                        result[index] =
                            (byte)((nibbles[index * 2] << 4) |
                                   nibbles[index * 2 + 1]);
                    }

                    return result;
                }

                if (IsWhiteSpace(value))
                {
                    continue;
                }

                nibbles.Add(ReadHexNibble(value));
            }

            throw new InvalidDataException(
                "PDF ToUnicode CMap содержит незакрытый hex-string.");
        }

        private void SkipWhiteSpaceAndComments()
        {
            while (position_ < data_.Length)
            {
                if (IsWhiteSpace(data_[position_]))
                {
                    position_++;
                    continue;
                }

                if (data_[position_] == (byte)'%')
                {
                    while (position_ < data_.Length &&
                           data_[position_] is not (byte)'\r' and not (byte)'\n')
                    {
                        position_++;
                    }

                    continue;
                }

                break;
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
                "PDF ToUnicode CMap содержит некорректную hex-цифру.");
        }
    }

    private sealed record CMapToken(
        CMapTokenKind Kind,
        string? Value,
        byte[]? Bytes);

    private enum CMapTokenKind
    {
        Word,
        Hex,
        LeftBracket,
        RightBracket
    }
}
