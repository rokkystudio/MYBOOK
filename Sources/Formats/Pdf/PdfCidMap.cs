using System.Globalization;
using System.IO;
using System.Text;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Преобразует variable-length character codes Type0 Encoding CMap в CID.
/// Поддерживает codespacerange, cidchar и cidrange.
/// </summary>
internal sealed class PdfCidMap
{
    private readonly IReadOnlyDictionary<string, int> mappings_;
    private readonly int[] codeLengths_;

    private PdfCidMap(
        IReadOnlyDictionary<string, int> mappings,
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
    /// Создаёт code-to-CID mapping из декодированного Type0 Encoding CMap stream.
    /// </summary>
    public static PdfCidMap FromCMap(byte[] cmapData)
    {
        return new CMapParser(cmapData).Parse();
    }

    /// <summary>
    /// Читает следующий source code по наиболее длинному допустимому совпадению
    /// и возвращает соответствующий CID.
    /// </summary>
    public bool TryReadCid(
        byte[] data,
        ref int position,
        out int cid,
        out int codeLength)
    {
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

            if (!mappings_.TryGetValue(key, out cid))
            {
                continue;
            }

            position += length;
            codeLength = length;
            return true;
        }

        cid = 0;
        codeLength = 0;
        return false;
    }

    private sealed class CMapParser
    {
        private readonly Lexer lexer_;
        private readonly Dictionary<string, int> mappings_ =
            new(StringComparer.Ordinal);
        private readonly HashSet<int> codeLengths_ = new();

        public CMapParser(byte[] data)
        {
            lexer_ = new Lexer(data);
        }

        public PdfCidMap Parse()
        {
            Token? previous = null;

            while (lexer_.Read() is { } token)
            {
                if (token.Kind != TokenKind.Word)
                {
                    previous = token;
                    continue;
                }

                switch (token.Value)
                {
                    case "begincodespacerange":
                        ParseCodeSpaceRange(ReadCount(previous));
                        break;

                    case "begincidchar":
                        ParseCidChar(ReadCount(previous));
                        break;

                    case "begincidrange":
                        ParseCidRange(ReadCount(previous));
                        break;
                }

                previous = token;
            }

            if (mappings_.Count == 0)
            {
                throw new InvalidDataException(
                    "PDF Encoding CMap не содержит cidchar/cidrange mappings.");
            }

            return new PdfCidMap(
                mappings_,
                codeLengths_);
        }

        private void ParseCodeSpaceRange(int count)
        {
            for (var index = 0; index < count; index++)
            {
                var start = ReadHex();
                var end = ReadHex();

                if (start.Length == 0 ||
                    start.Length != end.Length)
                {
                    throw new InvalidDataException(
                        "PDF Encoding CMap codespacerange содержит несовместимые границы.");
                }

                codeLengths_.Add(start.Length);
            }

            ExpectWord("endcodespacerange");
        }

        private void ParseCidChar(int count)
        {
            for (var index = 0; index < count; index++)
            {
                var source = ReadHex();
                var cid = ReadInteger();

                codeLengths_.Add(source.Length);
                mappings_[Convert.ToHexString(source)] = cid;
            }

            ExpectWord("endcidchar");
        }

        private void ParseCidRange(int count)
        {
            for (var index = 0; index < count; index++)
            {
                var start = ReadHex();
                var end = ReadHex();
                var firstCid = ReadInteger();

                if (start.Length == 0 ||
                    start.Length != end.Length)
                {
                    throw new InvalidDataException(
                        "PDF Encoding CMap cidrange содержит несовместимые source-коды.");
                }

                codeLengths_.Add(start.Length);

                var startValue = ReadUnsignedBigEndian(start);
                var endValue = ReadUnsignedBigEndian(end);

                if (endValue < startValue)
                {
                    throw new InvalidDataException(
                        "PDF Encoding CMap cidrange имеет обратный диапазон.");
                }

                var sourceValue = startValue;
                var cid = firstCid;

                while (true)
                {
                    mappings_[FormatSource(
                        sourceValue,
                        start.Length)] = cid;

                    if (sourceValue == endValue)
                    {
                        break;
                    }

                    if (cid == int.MaxValue)
                    {
                        throw new InvalidDataException(
                            "PDF Encoding CMap cidrange переполняет CID.");
                    }

                    sourceValue++;
                    cid++;
                }
            }

            ExpectWord("endcidrange");
        }

        private byte[] ReadHex()
        {
            var token = lexer_.Read()
                        ?? throw new InvalidDataException(
                            "Неожиданный конец PDF Encoding CMap.");

            if (token.Kind != TokenKind.Hex)
            {
                throw new InvalidDataException(
                    "PDF Encoding CMap ожидал hex-string.");
            }

            return token.Bytes!;
        }

        private int ReadInteger()
        {
            var token = lexer_.Read()
                        ?? throw new InvalidDataException(
                            "Неожиданный конец PDF Encoding CMap.");

            if (token.Kind != TokenKind.Word ||
                !int.TryParse(
                    token.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                value < 0)
            {
                throw new InvalidDataException(
                    "PDF Encoding CMap ожидал неотрицательный CID.");
            }

            return value;
        }

        private void ExpectWord(string value)
        {
            var token = lexer_.Read();

            if (token?.Kind != TokenKind.Word ||
                !string.Equals(
                    token.Value,
                    value,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"PDF Encoding CMap не содержит ожидаемый {value}.");
            }
        }

        private static int ReadCount(Token? token)
        {
            if (token?.Kind != TokenKind.Word ||
                !int.TryParse(
                    token.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var count) ||
                count < 0)
            {
                throw new InvalidDataException(
                    "PDF Encoding CMap не содержит корректный count перед begin-оператором.");
            }

            return count;
        }

        private static ulong ReadUnsignedBigEndian(byte[] data)
        {
            if (data.Length > sizeof(ulong))
            {
                throw new InvalidDataException(
                    "PDF Encoding CMap source code длиннее 8 байт.");
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
                    "PDF Encoding CMap source code длиннее 8 байт.");
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
    }

    private sealed class Lexer
    {
        private readonly byte[] data_;
        private int position_;

        public Lexer(byte[] data)
        {
            data_ = data;
        }

        public Token? Read()
        {
            SkipWhiteSpaceAndComments();

            if (position_ >= data_.Length)
            {
                return null;
            }

            if (data_[position_] == (byte)'<')
            {
                return new Token(
                    TokenKind.Hex,
                    null,
                    ReadHex());
            }

            var start = position_;

            while (position_ < data_.Length &&
                   !IsWhiteSpace(data_[position_]) &&
                   data_[position_] != (byte)'<')
            {
                position_++;
            }

            if (position_ == start)
            {
                throw new InvalidDataException(
                    "PDF Encoding CMap содержит неизвестный токен.");
            }

            return new Token(
                TokenKind.Word,
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
                "PDF Encoding CMap содержит незакрытый hex-string.");
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
                "PDF Encoding CMap содержит некорректную hex-цифру.");
        }
    }

    private sealed record Token(
        TokenKind Kind,
        string? Value,
        byte[]? Bytes);

    private enum TokenKind
    {
        Word,
        Hex
    }
}
