using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using MYBOOK.Documents;

namespace MYBOOK.Formats.Pdf;

/// <summary>
/// Читает структуру PDF через classic xref tables и xref streams,
/// поддерживает incremental xref-цепочки, object streams, page tree и content streams.
/// </summary>
internal sealed class PdfDocumentReader
{
    private readonly byte[] data_;
    private readonly Dictionary<int, XrefEntry> xref_;
    private readonly Dictionary<int, PdfObject> objectCache_ = new();
    private readonly Dictionary<int, Dictionary<int, PdfObject>> objectStreamCache_ = new();
    private readonly PdfDictionary trailer_;
    private readonly string version_;

    private PdfDocumentReader(
        byte[] data,
        Dictionary<int, XrefEntry> xref,
        PdfDictionary trailer,
        string version)
    {
        data_ = data;
        xref_ = xref;
        trailer_ = trailer;
        version_ = version;
    }

    /// <summary>
    /// Загружает PDF, объединяет classic/xref-stream секции и incremental updates,
    /// разрешает обычные и object-stream объекты и возвращает нейтральную модель документа.
    /// </summary>
    public static DocumentModel Read(string path)
    {
        var data = File.ReadAllBytes(path);
        var version = ReadVersion(data);
        var startXref = ReadStartXref(data);
        var xref = new Dictionary<int, XrefEntry>();
        var trailer = ReadXrefChain(
            data,
            startXref,
            xref,
            new HashSet<int>(),
            null);

        var reader = new PdfDocumentReader(data, xref, trailer, version);
        return reader.BuildDocument(path);
    }

    private DocumentModel BuildDocument(string path)
    {
        var rootReference = GetRequiredReference(trailer_, "Root");
        var catalog = GetRequiredDictionary(Resolve(rootReference), "PDF catalog");

        var pagesReference = GetRequiredReference(catalog, "Pages");
        var pages = new List<DocumentFixedPage>();

        ReadPageTree(
            pagesReference,
            pages,
            new PageInheritance());

        if (pages.Count == 0)
        {
            throw new InvalidDataException("PDF не содержит страниц.");
        }

        return new DocumentModel
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Blocks = pages.Cast<DocumentBlock>().ToArray()
        };
    }

    private void ReadPageTree(
        PdfReference reference,
        List<DocumentFixedPage> pages,
        PageInheritance inherited)
    {
        var dictionary = GetRequiredDictionary(
            Resolve(reference),
            $"PDF object {reference.ObjectNumber}");

        var type = GetName(dictionary, "Type");
        var current = MergePageInheritance(inherited, dictionary);

        if (string.Equals(type, "Page", StringComparison.Ordinal))
        {
            var box = current.CropBox ?? current.MediaBox
                      ?? throw new InvalidDataException(
                          $"PDF page {reference.ObjectNumber} не содержит MediaBox.");

            var width = Math.Abs(box.X2 - box.X1);
            var height = Math.Abs(box.Y2 - box.Y1);
            var rotation = NormalizeRotation(current.Rotate);

            if (rotation is 90 or 270)
            {
                (width, height) = (height, width);
            }

            pages.Add(new DocumentFixedPage
            {
                PageNumber = pages.Count + 1,
                WidthPoints = width,
                HeightPoints = height,
                RotationDegrees = rotation,
                Text = ReadPageText(dictionary, current.Resources)
            });

            return;
        }

        if (!string.Equals(type, "Pages", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PDF page tree содержит объект типа '{type ?? "<null>"}'.");
        }

        if (!dictionary.Items.TryGetValue("Kids", out var kidsObject))
        {
            throw new InvalidDataException("PDF Pages object не содержит Kids.");
        }

        var kids = ResolveIfReference(kidsObject) as PdfArray
                   ?? throw new InvalidDataException("PDF Pages/Kids не является массивом.");

        foreach (var child in kids.Items)
        {
            if (child is not PdfReference childReference)
            {
                throw new InvalidDataException("PDF Pages/Kids содержит не-reference.");
            }

            ReadPageTree(childReference, pages, current);
        }
    }

    private PageInheritance MergePageInheritance(
        PageInheritance inherited,
        PdfDictionary dictionary)
    {
        return new PageInheritance(
            ReadBox(dictionary, "MediaBox") ?? inherited.MediaBox,
            ReadBox(dictionary, "CropBox") ?? inherited.CropBox,
            TryGetInteger(dictionary, "Rotate", out var rotate)
                ? rotate
                : inherited.Rotate,
            ReadResources(dictionary) ?? inherited.Resources);
    }

    private PdfDictionary? ReadResources(PdfDictionary dictionary)
    {
        if (!dictionary.Items.TryGetValue("Resources", out var resourcesObject))
        {
            return null;
        }

        return ResolveIfReference(resourcesObject) as PdfDictionary
               ?? throw new InvalidDataException(
                   "PDF /Resources не является dictionary.");
    }

    private IReadOnlyDictionary<string, PdfFontDecoder> BuildFontDecoders(
        PdfDictionary? resources)
    {
        if (resources == null ||
            !resources.Items.TryGetValue("Font", out var fontObject))
        {
            return new Dictionary<string, PdfFontDecoder>(
                StringComparer.Ordinal);
        }

        var fontDictionary = ResolveIfReference(fontObject) as PdfDictionary
                             ?? throw new InvalidDataException(
                                 "PDF /Resources /Font не является dictionary.");

        var result = new Dictionary<string, PdfFontDecoder>(
            StringComparer.Ordinal);

        foreach (var pair in fontDictionary.Items)
        {
            var font = ResolveIfReference(pair.Value) as PdfDictionary
                       ?? throw new InvalidDataException(
                           $"PDF font /{pair.Key} не является dictionary.");

            if (!font.Items.TryGetValue("ToUnicode", out var toUnicodeObject))
            {
                continue;
            }

            var toUnicode = ResolveIfReference(toUnicodeObject) as PdfStream
                            ?? throw new InvalidDataException(
                                $"PDF font /{pair.Key} /ToUnicode не является stream.");

            result[pair.Key] = PdfFontDecoder.FromToUnicode(
                DecodeStream(toUnicode));
        }

        return result;
    }

    private string ReadPageText(
        PdfDictionary page,
        PdfDictionary? resources)
    {
        if (!page.Items.TryGetValue("Contents", out var contents))
        {
            return string.Empty;
        }

        var streams = new List<PdfStream>();
        CollectStreams(contents, streams);

        if (streams.Count == 0)
        {
            return string.Empty;
        }

        var output = new StringBuilder();
        var fontDecoders = BuildFontDecoders(resources);

        foreach (var stream in streams)
        {
            var decoded = DecodeStream(stream);

            if (output.Length > 0)
            {
                output.AppendLine();
            }

            output.Append(
                PdfTextExtractor.Extract(
                    decoded,
                    fontDecoders));
        }

        return output.ToString().Trim();
    }

    private void CollectStreams(PdfObject value, List<PdfStream> result)
    {
        var resolved = ResolveIfReference(value);

        switch (resolved)
        {
            case PdfStream stream:
                result.Add(stream);
                return;

            case PdfArray array:
                foreach (var item in array.Items)
                {
                    CollectStreams(item, result);
                }
                return;

            default:
                throw new InvalidDataException("PDF Contents не является stream или массивом streams.");
        }
    }

    private byte[] DecodeStream(PdfStream stream)
    {
        if (!stream.Dictionary.Items.TryGetValue("Filter", out var filterObject))
        {
            return stream.Data;
        }

        var resolved = ResolveIfReference(filterObject);

        if (resolved is PdfName name)
        {
            return DecodeFilter(name.Value, stream.Data);
        }

        if (resolved is PdfArray filters)
        {
            var data = stream.Data;

            foreach (var filter in filters.Items)
            {
                var filterName = ResolveIfReference(filter) as PdfName
                                 ?? throw new InvalidDataException(
                                     "PDF Filter array содержит не-name.");

                data = DecodeFilter(filterName.Value, data);
            }

            return data;
        }

        throw new InvalidDataException("PDF Filter имеет неподдерживаемый тип.");
    }

    private static byte[] DecodeFilter(string filter, byte[] data)
    {
        if (!string.Equals(filter, "FlateDecode", StringComparison.Ordinal) &&
            !string.Equals(filter, "Fl", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PDF stream filter '{filter}' пока не поддерживается.");
        }

        using var input = new MemoryStream(data, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private PdfObject Resolve(PdfReference reference)
    {
        if (objectCache_.TryGetValue(reference.ObjectNumber, out var cached))
        {
            return cached;
        }

        if (!xref_.TryGetValue(reference.ObjectNumber, out var entry))
        {
            throw new InvalidDataException(
                $"PDF xref не содержит объект {reference.ObjectNumber}.");
        }

        var value = entry.Type == 1
            ? ReadIndirectObject(
                new PdfReference(reference.ObjectNumber, entry.Field3),
                entry.Field2)
            : ResolveCompressedObject(reference.ObjectNumber, entry);
        objectCache_[reference.ObjectNumber] = value;
        return value;
    }

    private PdfObject ResolveCompressedObject(
        int objectNumber,
        XrefEntry entry)
    {
        if (entry.Type != 2)
        {
            throw new InvalidDataException(
                $"PDF object {objectNumber} имеет неподдерживаемый xref type {entry.Type}.");
        }

        var objectStreamNumber = checked((int)entry.Field2);

        if (!objectStreamCache_.TryGetValue(objectStreamNumber, out var objects))
        {
            objects = ReadObjectStream(objectStreamNumber);
            objectStreamCache_[objectStreamNumber] = objects;
        }

        if (!objects.TryGetValue(objectNumber, out var value))
        {
            throw new InvalidDataException(
                $"PDF object stream {objectStreamNumber} не содержит object {objectNumber}.");
        }

        return value;
    }

    private Dictionary<int, PdfObject> ReadObjectStream(
        int objectStreamNumber)
    {
        if (!xref_.TryGetValue(objectStreamNumber, out var entry) ||
            entry.Type != 1)
        {
            throw new InvalidDataException(
                $"PDF object stream {objectStreamNumber} не имеет обычного xref offset.");
        }

        var stream = ReadIndirectObject(
            new PdfReference(objectStreamNumber, entry.Field3),
            entry.Field2) as PdfStream
            ?? throw new InvalidDataException(
                $"PDF object {objectStreamNumber} не является stream.");

        if (!string.Equals(
                GetName(stream.Dictionary, "Type"),
                "ObjStm",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PDF object {objectStreamNumber} не имеет /Type /ObjStm.");
        }

        var count = GetRequiredInteger(stream.Dictionary, "N");
        var first = GetRequiredInteger(stream.Dictionary, "First");

        if (count < 0 || first < 0)
        {
            throw new InvalidDataException(
                "PDF ObjStm содержит отрицательные N/First.");
        }

        var decoded = DecodeStream(stream);

        if (first > decoded.Length)
        {
            throw new InvalidDataException(
                "PDF ObjStm /First выходит за границы stream.");
        }

        var headerPosition = 0;
        var descriptors = new List<(int ObjectNumber, int Offset)>(count);

        for (var index = 0; index < count; index++)
        {
            var number = ReadIntegerToken(decoded, ref headerPosition);
            var relativeOffset = ReadIntegerToken(decoded, ref headerPosition);

            if (number < 0 || relativeOffset < 0)
            {
                throw new InvalidDataException(
                    "PDF ObjStm header содержит отрицательное значение.");
            }

            descriptors.Add((number, relativeOffset));
        }

        var result = new Dictionary<int, PdfObject>();

        foreach (var descriptor in descriptors)
        {
            var objectPosition = checked(first + descriptor.Offset);

            if (objectPosition < first || objectPosition >= decoded.Length)
            {
                throw new InvalidDataException(
                    $"PDF ObjStm object {descriptor.ObjectNumber} выходит за границы stream.");
            }

            var syntax = new PdfSyntaxReader(decoded, objectPosition);
            result[descriptor.ObjectNumber] = syntax.ReadObject();
        }

        return result;
    }

    private PdfObject ResolveIfReference(PdfObject value)
    {
        return value is PdfReference reference
            ? Resolve(reference)
            : value;
    }

    private PdfObject ReadIndirectObject(PdfReference expectedReference, long offset)
    {
        if (offset < 0 || offset > int.MaxValue || offset >= data_.Length)
        {
            throw new InvalidDataException("PDF xref offset выходит за границы файла.");
        }

        var position = checked((int)offset);
        var objectNumber = ReadIntegerToken(data_, ref position);
        var generation = ReadIntegerToken(data_, ref position);
        var marker = ReadAsciiToken(data_, ref position);

        if (objectNumber != expectedReference.ObjectNumber ||
            generation != expectedReference.Generation ||
            !string.Equals(marker, "obj", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PDF xref offset для {expectedReference.ObjectNumber} {expectedReference.Generation} R указывает не на ожидаемый object.");
        }

        var syntax = new PdfSyntaxReader(data_, position);
        var value = syntax.ReadObject();
        position = syntax.Position;
        SkipWhiteSpace(data_, ref position);

        if (value is PdfDictionary dictionary &&
            StartsWith(data_, position, "stream"))
        {
            position += "stream".Length;

            if (position < data_.Length && data_[position] == (byte)'\r')
            {
                position++;
                if (position < data_.Length && data_[position] == (byte)'\n')
                {
                    position++;
                }
            }
            else if (position < data_.Length && data_[position] == (byte)'\n')
            {
                position++;
            }
            else
            {
                throw new InvalidDataException("PDF stream не отделён переводом строки.");
            }

            var length = ResolveStreamLength(dictionary);

            if (length >= 0)
            {
                if ((long)position + length > data_.Length)
                {
                    throw new InvalidDataException("PDF stream выходит за границы файла.");
                }

                return new PdfStream(
                    dictionary,
                    data_.AsSpan(position, length).ToArray());
            }

            var endStream = FindAscii(data_, "endstream", position);
            if (endStream < 0)
            {
                throw new InvalidDataException("PDF stream не содержит endstream.");
            }

            var end = endStream;
            while (end > position &&
                   data_[end - 1] is (byte)'\r' or (byte)'\n')
            {
                end--;
            }

            return new PdfStream(
                dictionary,
                data_.AsSpan(position, end - position).ToArray());
        }

        return value;
    }

    private int ResolveStreamLength(PdfDictionary dictionary)
    {
        if (!dictionary.Items.TryGetValue("Length", out var lengthObject))
        {
            return -1;
        }

        var resolved = ResolveIfReference(lengthObject);

        if (resolved is not PdfNumber number ||
            number.Value < 0 ||
            number.Value != Math.Truncate(number.Value) ||
            number.Value > int.MaxValue)
        {
            throw new InvalidDataException("PDF stream Length имеет недопустимое значение.");
        }

        return (int)number.Value;
    }

    private static string ReadVersion(byte[] data)
    {
        if (data.Length < 8 ||
            !StartsWith(data, 0, "%PDF-"))
        {
            throw new InvalidDataException("Файл не содержит PDF header.");
        }

        var end = 5;

        while (end < data.Length &&
               data[end] is not (byte)'\r' and not (byte)'\n' &&
               end < 16)
        {
            end++;
        }

        var version = Encoding.ASCII.GetString(data, 5, end - 5);

        if (version.Length == 0)
        {
            throw new InvalidDataException("PDF header не содержит версию.");
        }

        return version;
    }

    private static int ReadStartXref(byte[] data)
    {
        var marker = Encoding.ASCII.GetBytes("startxref");
        var start = Math.Max(0, data.Length - 65536);

        for (var position = data.Length - marker.Length; position >= start; position--)
        {
            if (!data.AsSpan(position, marker.Length).SequenceEqual(marker))
            {
                continue;
            }

            var cursor = position + marker.Length;
            SkipWhiteSpace(data, ref cursor);
            var value = ReadIntegerToken(data, ref cursor);

            if (value < 0 || value >= data.Length)
            {
                throw new InvalidDataException("startxref содержит недопустимый offset.");
            }

            return value;
        }

        throw new InvalidDataException("PDF не содержит startxref.");
    }

    private static PdfDictionary ReadXrefChain(
        byte[] data,
        int offset,
        Dictionary<int, XrefEntry> entries,
        HashSet<int> visitedOffsets,
        PdfDictionary? newestTrailer)
    {
        if (!visitedOffsets.Add(offset))
        {
            throw new InvalidDataException("PDF xref /Prev образует цикл.");
        }

        SkipWhiteSpace(data, ref offset);

        PdfDictionary currentTrailer;

        if (StartsWith(data, offset, "xref"))
        {
            var (sectionEntries, trailer) = ReadClassicXref(data, offset);

            foreach (var pair in sectionEntries)
            {
                if (!entries.ContainsKey(pair.Key))
                {
                    entries[pair.Key] = pair.Value;
                }
            }

            currentTrailer = trailer;
        }
        else
        {
            currentTrailer = ReadXrefStream(data, offset, entries);
        }

        newestTrailer ??= currentTrailer;

        if (TryGetInteger(currentTrailer, "Prev", out var previousOffset))
        {
            if (previousOffset < 0 || previousOffset >= data.Length)
            {
                throw new InvalidDataException(
                    "PDF trailer /Prev выходит за границы файла.");
            }

            ReadXrefChain(
                data,
                previousOffset,
                entries,
                visitedOffsets,
                newestTrailer);
        }

        if (TryGetInteger(currentTrailer, "XRefStm", out var xrefStreamOffset) &&
            !visitedOffsets.Contains(xrefStreamOffset))
        {
            if (xrefStreamOffset < 0 || xrefStreamOffset >= data.Length)
            {
                throw new InvalidDataException(
                    "PDF trailer /XRefStm выходит за границы файла.");
            }

            ReadXrefChain(
                data,
                xrefStreamOffset,
                entries,
                visitedOffsets,
                newestTrailer);
        }

        return newestTrailer;
    }

    private static PdfDictionary ReadXrefStream(
        byte[] data,
        int offset,
        Dictionary<int, XrefEntry> entries)
    {
        var position = offset;
        ReadIntegerToken(data, ref position);
        ReadIntegerToken(data, ref position);

        if (!string.Equals(
                ReadAsciiToken(data, ref position),
                "obj",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "startxref не указывает на indirect object.");
        }

        var syntax = new PdfSyntaxReader(data, position);
        var dictionary = syntax.ReadObject() as PdfDictionary
                         ?? throw new InvalidDataException(
                             "xref stream object не является dictionary.");

        if (!string.Equals(
                GetName(dictionary, "Type"),
                "XRef",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "xref stream не имеет /Type /XRef.");
        }

        var streamData = ReadStandaloneStream(
            data,
            syntax.Position,
            dictionary);

        var widths = GetIntegerArray(dictionary, "W");
        if (widths.Length != 3 ||
            widths.Any(width => width < 0 || width > 8))
        {
            throw new InvalidDataException(
                "xref stream /W должен содержать три ширины 0..8.");
        }

        var size = GetRequiredInteger(dictionary, "Size");
        var index = dictionary.Items.ContainsKey("Index")
            ? GetIntegerArray(dictionary, "Index")
            : new[] { 0, size };

        if ((index.Length & 1) != 0)
        {
            throw new InvalidDataException(
                "xref stream /Index должен содержать пары.");
        }

        var rowSize = widths.Sum();
        var cursor = 0;

        for (var pair = 0; pair < index.Length; pair += 2)
        {
            var firstObject = index[pair];
            var count = index[pair + 1];

            for (var item = 0; item < count; item++)
            {
                if (cursor + rowSize > streamData.Length)
                {
                    throw new InvalidDataException(
                        "xref stream короче /Index.");
                }

                var type = widths[0] == 0
                    ? 1
                    : checked((int)ReadBigEndian(
                        streamData,
                        ref cursor,
                        widths[0]));

                var field2 = ReadBigEndian(
                    streamData,
                    ref cursor,
                    widths[1]);

                var field3 = checked((int)ReadBigEndian(
                    streamData,
                    ref cursor,
                    widths[2]));

                var objectNumber = firstObject + item;

                if (!entries.ContainsKey(objectNumber))
                {
                    entries[objectNumber] = new XrefEntry(
                        type,
                        checked((long)field2),
                        field3);
                }
            }
        }

        return dictionary;
    }

    private static byte[] ReadStandaloneStream(
        byte[] data,
        int position,
        PdfDictionary dictionary)
    {
        SkipWhiteSpace(data, ref position);

        if (!StartsWith(data, position, "stream"))
        {
            throw new InvalidDataException(
                "xref object не содержит stream.");
        }

        position += "stream".Length;

        if (position < data.Length && data[position] == (byte)'\r')
        {
            position++;
            if (position < data.Length && data[position] == (byte)'\n')
            {
                position++;
            }
        }
        else if (position < data.Length && data[position] == (byte)'\n')
        {
            position++;
        }
        else
        {
            throw new InvalidDataException(
                "xref stream не отделён переводом строки.");
        }

        int length = -1;

        if (dictionary.Items.TryGetValue("Length", out var lengthObject) &&
            lengthObject is PdfNumber number &&
            number.Value >= 0 &&
            number.Value == Math.Truncate(number.Value) &&
            number.Value <= int.MaxValue)
        {
            length = (int)number.Value;
        }

        byte[] streamData;

        if (length >= 0)
        {
            if ((long)position + length > data.Length)
            {
                throw new InvalidDataException(
                    "xref stream выходит за границы файла.");
            }

            streamData = data.AsSpan(position, length).ToArray();
        }
        else
        {
            var end = FindAscii(data, "endstream", position);

            if (end < 0)
            {
                throw new InvalidDataException(
                    "xref stream не содержит endstream.");
            }

            var streamEnd = end;

            while (streamEnd > position &&
                   data[streamEnd - 1] is (byte)'\r' or (byte)'\n')
            {
                streamEnd--;
            }

            streamData = data.AsSpan(
                position,
                streamEnd - position).ToArray();
        }

        if (!dictionary.Items.TryGetValue("Filter", out var filter))
        {
            return streamData;
        }

        if (filter is PdfName filterName)
        {
            return DecodeFilter(
                filterName.Value,
                streamData);
        }

        if (filter is PdfArray filters)
        {
            var decoded = streamData;

            foreach (var item in filters.Items)
            {
                if (item is not PdfName name)
                {
                    throw new InvalidDataException(
                        "xref stream Filter array содержит не-name.");
                }

                decoded = DecodeFilter(
                    name.Value,
                    decoded);
            }

            return decoded;
        }

        throw new InvalidDataException(
            "xref stream /Filter имеет неподдерживаемый тип.");
    }

    private static ulong ReadBigEndian(
        byte[] data,
        ref int position,
        int width)
    {
        ulong value = 0;

        for (var index = 0; index < width; index++)
        {
            value = (value << 8) | data[position++];
        }

        return value;
    }

    private static (Dictionary<int, XrefEntry> Xref, PdfDictionary Trailer) ReadClassicXref(
        byte[] data,
        int offset)
    {
        var position = offset;
        var token = ReadAsciiToken(data, ref position);

        if (!string.Equals(token, "xref", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "PDF использует xref stream или неподдерживаемую структуру; classic xref table не найден.");
        }

        var entries = new Dictionary<int, XrefEntry>();

        while (true)
        {
            SkipWhiteSpace(data, ref position);

            if (StartsWith(data, position, "trailer"))
            {
                position += "trailer".Length;
                var syntax = new PdfSyntaxReader(data, position);
                var trailer = syntax.ReadObject() as PdfDictionary
                              ?? throw new InvalidDataException("PDF trailer не является dictionary.");

                return (entries, trailer);
            }

            var firstObject = ReadIntegerToken(data, ref position);
            var count = ReadIntegerToken(data, ref position);

            if (firstObject < 0 || count < 0)
            {
                throw new InvalidDataException("PDF xref subsection содержит отрицательные значения.");
            }

            for (var index = 0; index < count; index++)
            {
                var objectOffsetToken = ReadAsciiToken(data, ref position);
                var generationToken = ReadAsciiToken(data, ref position);
                var status = ReadAsciiToken(data, ref position);

                if (!long.TryParse(
                        objectOffsetToken,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var objectOffset) ||
                    !int.TryParse(
                        generationToken,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var generation))
                {
                    throw new InvalidDataException("PDF xref entry содержит некорректное число.");
                }

                entries[firstObject + index] = string.Equals(
                    status,
                    "n",
                    StringComparison.Ordinal)
                    ? new XrefEntry(1, objectOffset, generation)
                    : new XrefEntry(0, objectOffset, generation);
            }
        }
    }

    private static PageBox? ReadBox(
        PdfDictionary dictionary,
        string key)
    {
        if (!dictionary.Items.TryGetValue(key, out var boxObject))
        {
            return null;
        }

        if (boxObject is not PdfArray array || array.Items.Count != 4)
        {
            throw new InvalidDataException(
                $"PDF {key} должен содержать четыре числа.");
        }

        var values = array.Items
            .Select(item => item as PdfNumber
                            ?? throw new InvalidDataException(
                                $"PDF {key} содержит нечисловое значение."))
            .Select(number => number.Value)
            .ToArray();

        return new PageBox(
            values[0],
            values[1],
            values[2],
            values[3]);
    }

    private static int NormalizeRotation(int rotation)
    {
        var normalized = rotation % 360;

        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized is 0 or 90 or 180 or 270
            ? normalized
            : throw new InvalidDataException(
                $"PDF /Rotate имеет неподдерживаемое значение {rotation}.");
    }

    private static PdfReference GetRequiredReference(
        PdfDictionary dictionary,
        string key)
    {
        if (!dictionary.Items.TryGetValue(key, out var value) ||
            value is not PdfReference reference)
        {
            throw new InvalidDataException($"PDF dictionary не содержит reference /{key}.");
        }

        return reference;
    }

    private static PdfDictionary GetRequiredDictionary(PdfObject value, string context)
    {
        return value as PdfDictionary
               ?? throw new InvalidDataException($"{context} не является dictionary.");
    }

    private static string? GetName(PdfDictionary dictionary, string key)
    {
        return dictionary.Items.TryGetValue(key, out var value) &&
               value is PdfName name
            ? name.Value
            : null;
    }

    private static int GetRequiredInteger(
        PdfDictionary dictionary,
        string key)
    {
        if (!dictionary.Items.TryGetValue(key, out var value) ||
            value is not PdfNumber number ||
            number.Value != Math.Truncate(number.Value) ||
            number.Value < int.MinValue ||
            number.Value > int.MaxValue)
        {
            throw new InvalidDataException(
                $"PDF dictionary /{key} не является целым числом.");
        }

        return (int)number.Value;
    }

    private static bool TryGetInteger(
        PdfDictionary dictionary,
        string key,
        out int value)
    {
        value = 0;

        if (!dictionary.Items.TryGetValue(key, out var item) ||
            item is not PdfNumber number ||
            number.Value != Math.Truncate(number.Value) ||
            number.Value < int.MinValue ||
            number.Value > int.MaxValue)
        {
            return false;
        }

        value = (int)number.Value;
        return true;
    }

    private static int[] GetIntegerArray(
        PdfDictionary dictionary,
        string key)
    {
        if (!dictionary.Items.TryGetValue(key, out var value) ||
            value is not PdfArray array)
        {
            throw new InvalidDataException(
                $"PDF dictionary /{key} не является массивом.");
        }

        return array.Items
            .Select(item =>
            {
                if (item is not PdfNumber number ||
                    number.Value != Math.Truncate(number.Value) ||
                    number.Value < int.MinValue ||
                    number.Value > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"PDF /{key} содержит нецелое значение.");
                }

                return (int)number.Value;
            })
            .ToArray();
    }

    private static int ReadIntegerToken(byte[] data, ref int position)
    {
        var token = ReadAsciiToken(data, ref position);

        if (!int.TryParse(
                token,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new InvalidDataException($"Ожидалось целое PDF-число, получено '{token}'.");
        }

        return value;
    }

    private static string ReadAsciiToken(byte[] data, ref int position)
    {
        SkipWhiteSpace(data, ref position);

        if (position >= data.Length)
        {
            throw new EndOfStreamException("Неожиданный конец PDF.");
        }

        var start = position;

        while (position < data.Length &&
               !IsWhiteSpace(data[position]))
        {
            position++;
        }

        if (position == start)
        {
            throw new InvalidDataException("Пустой PDF token.");
        }

        return Encoding.ASCII.GetString(data, start, position - start);
    }

    private static void SkipWhiteSpace(byte[] data, ref int position)
    {
        while (position < data.Length)
        {
            if (IsWhiteSpace(data[position]))
            {
                position++;
                continue;
            }

            if (data[position] == (byte)'%')
            {
                while (position < data.Length &&
                       data[position] is not (byte)'\r' and not (byte)'\n')
                {
                    position++;
                }

                continue;
            }

            break;
        }
    }

    private static bool IsWhiteSpace(byte value)
    {
        return value is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;
    }

    private static bool StartsWith(byte[] data, int position, string value)
    {
        if (position < 0 || position + value.Length > data.Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (data[position + index] != (byte)value[index])
            {
                return false;
            }
        }

        return true;
    }

    private static int FindAscii(byte[] data, string value, int start)
    {
        var bytes = Encoding.ASCII.GetBytes(value);

        for (var position = start; position <= data.Length - bytes.Length; position++)
        {
            if (data.AsSpan(position, bytes.Length).SequenceEqual(bytes))
            {
                return position;
            }
        }

        return -1;
    }

    private sealed record XrefEntry(
        int Type,
        long Field2,
        int Field3);

    private sealed record PageBox(
        double X1,
        double Y1,
        double X2,
        double Y2);

    private sealed record PageInheritance(
        PageBox? MediaBox = null,
        PageBox? CropBox = null,
        int Rotate = 0,
        PdfDictionary? Resources = null);
}

/// <summary>
/// Извлекает текстовый слой из PDF content stream.
/// Поддерживает переключение font resource через Tf, ToUnicode CMap,
/// literal/hex strings, Tj, TJ, quote operators и переносы строк.
/// </summary>
internal static class PdfTextExtractor
{
    /// <summary>
    /// Извлекает последовательный текст из операторов PDF content stream.
    /// </summary>
    public static string Extract(
        byte[] content,
        IReadOnlyDictionary<string, PdfFontDecoder> fontDecoders)
    {
        var parser = new ContentParser(
            content,
            fontDecoders);

        return parser.Extract();
    }

    private sealed class ContentParser
    {
        private readonly byte[] data_;
        private readonly IReadOnlyDictionary<string, PdfFontDecoder> fontDecoders_;
        private int position_;
        private readonly List<Operand> operands_ = new();
        private readonly StringBuilder output_ = new();
        private PdfFontDecoder? currentFontDecoder_;

        public ContentParser(
            byte[] data,
            IReadOnlyDictionary<string, PdfFontDecoder> fontDecoders)
        {
            data_ = data;
            fontDecoders_ = fontDecoders;
        }

        public string Extract()
        {
            while (true)
            {
                SkipWhiteSpaceAndComments();

                if (position_ >= data_.Length)
                {
                    break;
                }

                var value = data_[position_];

                if (value == (byte)'(')
                {
                    operands_.Add(new StringOperand(ReadLiteralString()));
                    continue;
                }

                if (value == (byte)'<')
                {
                    if (position_ + 1 < data_.Length &&
                        data_[position_ + 1] == (byte)'<')
                    {
                        SkipDictionary();
                    }
                    else
                    {
                        operands_.Add(new StringOperand(ReadHexString()));
                    }
                    continue;
                }

                if (value == (byte)'[')
                {
                    operands_.Add(new ArrayOperand(ReadArray()));
                    continue;
                }

                if (IsNumberStart(value))
                {
                    operands_.Add(new NumberOperand(ReadNumber()));
                    continue;
                }

                if (value == (byte)'/')
                {
                    operands_.Add(
                        new NameOperand(ReadName()));
                    continue;
                }

                var token = ReadToken();
                ApplyOperator(token);
            }

            return NormalizeOutput(output_.ToString());
        }

        private void ApplyOperator(string token)
        {
            switch (token)
            {
                case "Tf":
                    SelectFont();
                    break;

                case "Tj":
                    AppendLastString();
                    break;

                case "TJ":
                    AppendLastArray();
                    break;

                case "'":
                    AppendLineBreak();
                    AppendLastString();
                    break;

                case "\\\"":
                    AppendLineBreak();
                    AppendLastString();
                    break;

                case "T*":
                case "Td":
                case "TD":
                    AppendLineBreak();
                    break;

                case "BT":
                    if (output_.Length > 0 &&
                        output_[^1] != '\n')
                    {
                        AppendLineBreak();
                    }
                    break;
            }

            operands_.Clear();
        }

        private void AppendLastString()
        {
            var value = operands_.LastOrDefault() as StringOperand;
            if (value != null)
            {
                output_.Append(DecodePdfString(value.Data));
            }
        }

        private void AppendLastArray()
        {
            var value = operands_.LastOrDefault() as ArrayOperand;
            if (value == null)
            {
                return;
            }

            foreach (var item in value.Items)
            {
                if (item is StringOperand text)
                {
                    output_.Append(DecodePdfString(text.Data));
                }
                else if (item is NumberOperand number && number.Value < -180)
                {
                    output_.Append(' ');
                }
            }
        }

        private void AppendLineBreak()
        {
            if (output_.Length == 0 || output_[^1] == '\n')
            {
                return;
            }

            output_.AppendLine();
        }

        private List<Operand> ReadArray()
        {
            position_++;
            var result = new List<Operand>();

            while (true)
            {
                SkipWhiteSpaceAndComments();

                if (position_ >= data_.Length)
                {
                    throw new InvalidDataException("Незакрытый массив в PDF content stream.");
                }

                var value = data_[position_];

                if (value == (byte)']')
                {
                    position_++;
                    return result;
                }

                if (value == (byte)'(')
                {
                    result.Add(new StringOperand(ReadLiteralString()));
                    continue;
                }

                if (value == (byte)'<')
                {
                    result.Add(new StringOperand(ReadHexString()));
                    continue;
                }

                if (IsNumberStart(value))
                {
                    result.Add(new NumberOperand(ReadNumber()));
                    continue;
                }

                ReadToken();
                result.Add(new OtherOperand());
            }
        }

        private byte[] ReadLiteralString()
        {
            position_++;
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
                            if (position_ < data_.Length &&
                                data_[position_] == (byte)'\n')
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

            throw new InvalidDataException("Незакрытая PDF string в content stream.");
        }

        private byte[] ReadHexString()
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
                        result[index] = (byte)((nibbles[index * 2] << 4) |
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

            throw new InvalidDataException("Незакрытая PDF hex string в content stream.");
        }

        private double ReadNumber()
        {
            var start = position_;

            if (data_[position_] is (byte)'+' or (byte)'-')
            {
                position_++;
            }

            while (position_ < data_.Length &&
                   (data_[position_] is >= (byte)'0' and <= (byte)'9' ||
                    data_[position_] == (byte)'.'))
            {
                position_++;
            }

            var token = Encoding.ASCII.GetString(
                data_,
                start,
                position_ - start);

            return double.Parse(
                token,
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private string ReadName()
        {
            position_++;
            var start = position_;

            while (position_ < data_.Length &&
                   !IsWhiteSpace(data_[position_]) &&
                   !IsDelimiter(data_[position_]))
            {
                position_++;
            }

            return Encoding.ASCII.GetString(
                data_,
                start,
                position_ - start);
        }

        private string ReadToken()
        {
            SkipWhiteSpaceAndComments();
            var start = position_;

            if (data_[position_] is (byte)'\'' or (byte)'"')
            {
                position_++;
                return ((char)data_[start]).ToString();
            }

            while (position_ < data_.Length &&
                   !IsWhiteSpace(data_[position_]) &&
                   !IsDelimiter(data_[position_]))
            {
                position_++;
            }

            if (position_ == start)
            {
                position_++;
                return ((char)data_[start]).ToString();
            }

            return Encoding.ASCII.GetString(
                data_,
                start,
                position_ - start);
        }

        private void SkipDictionary()
        {
            position_ += 2;
            var depth = 1;

            while (position_ < data_.Length && depth > 0)
            {
                if (position_ + 1 < data_.Length &&
                    data_[position_] == (byte)'<' &&
                    data_[position_ + 1] == (byte)'<')
                {
                    depth++;
                    position_ += 2;
                    continue;
                }

                if (position_ + 1 < data_.Length &&
                    data_[position_] == (byte)'>' &&
                    data_[position_ + 1] == (byte)'>')
                {
                    depth--;
                    position_ += 2;
                    continue;
                }

                position_++;
            }
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

        private void SelectFont()
        {
            var font = operands_
                .OfType<NameOperand>()
                .LastOrDefault();

            currentFontDecoder_ =
                font != null &&
                fontDecoders_.TryGetValue(font.Name, out var decoder)
                    ? decoder
                    : null;
        }

        private string DecodePdfString(byte[] data)
        {
            return currentFontDecoder_?.Decode(data)
                   ?? PdfFontDecoder.DecodeFallback(data);
        }

        private static string NormalizeOutput(string value)
        {
            var lines = value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();

            return string.Join(Environment.NewLine, lines);
        }

        private static bool IsNumberStart(byte value)
        {
            return value is >= (byte)'0' and <= (byte)'9' or
                   (byte)'+' or (byte)'-' or (byte)'.';
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

            throw new InvalidDataException("Некорректная hex-цифра в PDF.");
        }

        private abstract record Operand;
        private sealed record StringOperand(byte[] Data) : Operand;
        private sealed record ArrayOperand(IReadOnlyList<Operand> Items) : Operand;
        private sealed record NumberOperand(double Value) : Operand;
        private sealed record NameOperand(string Name) : Operand;
        private sealed record OtherOperand : Operand;
    }
}
