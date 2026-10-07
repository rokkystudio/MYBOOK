using System.IO;
using System.Globalization;
using System.Text;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Читает базовые синтаксические объекты PDF из массива байтов.
/// </summary>
internal sealed class PdfSyntaxReader
{
    private readonly byte[] data_;
    private int position_;

    public PdfSyntaxReader(byte[] data, int position = 0)
    {
        data_ = data;
        position_ = position;
    }

    public int Position => position_;

    /// <summary>
    /// Возвращает следующий PDF-объект, включая ссылки, словари, массивы и строки.
    /// </summary>
    public PdfObject ReadObject()
    {
        SkipWhiteSpaceAndComments();

        if (MatchKeyword("null"))
        {
            return new PdfNull();
        }

        if (MatchKeyword("true"))
        {
            return new PdfBoolean(true);
        }

        if (MatchKeyword("false"))
        {
            return new PdfBoolean(false);
        }

        if (Peek("<<"))
        {
            return ReadDictionary();
        }

        if (Peek("["))
        {
            return ReadArray();
        }

        if (Peek("/"))
        {
            return new PdfName(ReadName());
        }

        if (Peek("("))
        {
            return new PdfString(ReadLiteralString());
        }

        if (Peek("<"))
        {
            return new PdfString(ReadHexString());
        }

        var start = position_;
        var first = ReadNumberToken();

        if (first == null)
        {
            throw new InvalidDataException($"Неожиданный PDF-токен в позиции {position_}.");
        }

        SkipWhiteSpaceAndComments();
        var afterFirst = position_;
        var second = ReadIntegerToken();

        if (second.HasValue)
        {
            SkipWhiteSpaceAndComments();
            if (MatchKeyword("R"))
            {
                return new PdfReference(
                    checked((int)first.Value),
                    second.Value);
            }
        }

        position_ = afterFirst;
        return new PdfNumber(first.Value);
    }

    /// <summary>
    /// Читает PDF-словарь из текущей позиции.
    /// </summary>
    public PdfDictionary ReadDictionary()
    {
        Expect("<<");
        var items = new Dictionary<string, PdfObject>(StringComparer.Ordinal);

        while (true)
        {
            SkipWhiteSpaceAndComments();

            if (Peek(">>"))
            {
                position_ += 2;
                return new PdfDictionary(items);
            }

            if (!Peek("/"))
            {
                throw new InvalidDataException(
                    $"PDF dictionary ожидает имя ключа в позиции {position_}.");
            }

            var name = ReadName();
            items[name] = ReadObject();
        }
    }

    /// <summary>
    /// Пропускает пробелы и комментарии PDF.
    /// </summary>
    public void SkipWhiteSpaceAndComments()
    {
        while (position_ < data_.Length)
        {
            var value = data_[position_];

            if (IsWhiteSpace(value))
            {
                position_++;
                continue;
            }

            if (value == (byte)'%')
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

    /// <summary>
    /// Проверяет и потребляет заданное ASCII-слово.
    /// </summary>
    public bool MatchKeyword(string keyword)
    {
        SkipWhiteSpaceAndComments();

        if (!Peek(keyword))
        {
            return false;
        }

        var end = position_ + keyword.Length;
        if (end < data_.Length && !IsDelimiter(data_[end]) && !IsWhiteSpace(data_[end]))
        {
            return false;
        }

        position_ = end;
        return true;
    }

    /// <summary>
    /// Требует заданный ASCII-токен в текущей позиции.
    /// </summary>
    public void Expect(string token)
    {
        SkipWhiteSpaceAndComments();

        if (!Peek(token))
        {
            throw new InvalidDataException(
                $"Ожидался PDF-токен '{token}' в позиции {position_}.");
        }

        position_ += token.Length;
    }

    /// <summary>
    /// Возвращает следующий непробельный byte без продвижения позиции.
    /// </summary>
    public byte PeekByte()
    {
        SkipWhiteSpaceAndComments();

        if (position_ >= data_.Length)
        {
            throw new EndOfStreamException("Неожиданный конец PDF.");
        }

        return data_[position_];
    }

    private PdfArray ReadArray()
    {
        Expect("[");
        var items = new List<PdfObject>();

        while (true)
        {
            SkipWhiteSpaceAndComments();

            if (Peek("]"))
            {
                position_++;
                return new PdfArray(items);
            }

            items.Add(ReadObject());
        }
    }

    private string ReadName()
    {
        Expect("/");
        var bytes = new List<byte>();

        while (position_ < data_.Length)
        {
            var value = data_[position_];

            if (IsWhiteSpace(value) || IsDelimiter(value))
            {
                break;
            }

            if (value == (byte)'#' &&
                position_ + 2 < data_.Length &&
                TryHex(data_[position_ + 1], out var high) &&
                TryHex(data_[position_ + 2], out var low))
            {
                bytes.Add((byte)((high << 4) | low));
                position_ += 3;
                continue;
            }

            bytes.Add(value);
            position_++;
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private byte[] ReadLiteralString()
    {
        Expect("(");
        var result = new List<byte>();
        var depth = 1;

        while (position_ < data_.Length)
        {
            var value = data_[position_++];

            if (value == (byte)'\\')
            {
                if (position_ >= data_.Length)
                {
                    break;
                }

                var escaped = data_[position_++];

                switch (escaped)
                {
                    case (byte)'n': result.Add((byte)'\n'); break;
                    case (byte)'r': result.Add((byte)'\r'); break;
                    case (byte)'t': result.Add((byte)'\t'); break;
                    case (byte)'b': result.Add((byte)'\b'); break;
                    case (byte)'f': result.Add((byte)'\f'); break;
                    case (byte)'(':
                    case (byte)')':
                    case (byte)'\\':
                        result.Add(escaped);
                        break;
                    case (byte)'\r':
                        if (position_ < data_.Length && data_[position_] == (byte)'\n')
                        {
                            position_++;
                        }
                        break;
                    case (byte)'\n':
                        break;
                    default:
                        if (escaped is >= (byte)'0' and <= (byte)'7')
                        {
                            var octal = escaped - (byte)'0';
                            var count = 1;

                            while (count < 3 &&
                                   position_ < data_.Length &&
                                   data_[position_] is >= (byte)'0' and <= (byte)'7')
                            {
                                octal = octal * 8 + data_[position_] - (byte)'0';
                                position_++;
                                count++;
                            }

                            result.Add((byte)octal);
                        }
                        else
                        {
                            result.Add(escaped);
                        }
                        break;
                }

                continue;
            }

            if (value == (byte)'(')
            {
                depth++;
                result.Add(value);
                continue;
            }

            if (value == (byte)')')
            {
                depth--;
                if (depth == 0)
                {
                    return result.ToArray();
                }

                result.Add(value);
                continue;
            }

            result.Add(value);
        }

        throw new InvalidDataException("Незакрытая literal string в PDF.");
    }

    private byte[] ReadHexString()
    {
        Expect("<");
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
                    result[index] = (byte)((nibbles[index * 2] << 4) |
                                           nibbles[index * 2 + 1]);
                }

                return result;
            }

            if (IsWhiteSpace(value))
            {
                continue;
            }

            if (!TryHex(value, out var nibble))
            {
                throw new InvalidDataException("Недопустимый символ hex string в PDF.");
            }

            nibbles.Add(nibble);
        }

        throw new InvalidDataException("Незакрытая hex string в PDF.");
    }

    private double? ReadNumberToken()
    {
        SkipWhiteSpaceAndComments();

        var start = position_;

        if (position_ < data_.Length &&
            data_[position_] is (byte)'+' or (byte)'-')
        {
            position_++;
        }

        var hasDigit = false;
        var hasDot = false;

        while (position_ < data_.Length)
        {
            var current = data_[position_];

            if (current is >= (byte)'0' and <= (byte)'9')
            {
                hasDigit = true;
                position_++;
                continue;
            }

            if (current == (byte)'.' && !hasDot)
            {
                hasDot = true;
                position_++;
                continue;
            }

            break;
        }

        if (!hasDigit)
        {
            position_ = start;
            return null;
        }

        var token = Encoding.ASCII.GetString(data_, start, position_ - start);

        return double.TryParse(
            token,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new InvalidDataException($"Некорректное PDF-число '{token}'.");
    }

    private int? ReadIntegerToken()
    {
        var start = position_;
        var number = ReadNumberToken();

        if (!number.HasValue ||
            number.Value != Math.Truncate(number.Value) ||
            number.Value < int.MinValue ||
            number.Value > int.MaxValue)
        {
            position_ = start;
            return null;
        }

        return (int)number.Value;
    }

    private bool Peek(string token)
    {
        if (position_ + token.Length > data_.Length)
        {
            return false;
        }

        for (var index = 0; index < token.Length; index++)
        {
            if (data_[position_ + index] != (byte)token[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWhiteSpace(byte value)
    {
        return value is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
    }

    private static bool IsDelimiter(byte value)
    {
        return value is (byte)'(' or (byte)')' or
               (byte)'<' or (byte)'>' or
               (byte)'[' or (byte)']' or
               (byte)'{' or (byte)'}' or
               (byte)'/' or (byte)'%';
    }

    private static bool TryHex(byte value, out int result)
    {
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            result = value - (byte)'0';
            return true;
        }

        if (value is >= (byte)'A' and <= (byte)'F')
        {
            result = value - (byte)'A' + 10;
            return true;
        }

        if (value is >= (byte)'a' and <= (byte)'f')
        {
            result = value - (byte)'a' + 10;
            return true;
        }

        result = 0;
        return false;
    }
}
