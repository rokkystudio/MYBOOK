using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MYBOOK.Formats.Doc;

/// <summary>
/// Читает Microsoft Compound File Binary Format (OLE/CFB),
/// включая FAT, MiniFAT, каталог и обычные/мини-потоки.
/// </summary>
internal sealed class CompoundBinaryFile
{
    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;

    private readonly byte[] data_;
    private readonly int sectorSize_;
    private readonly int miniSectorSize_;
    private readonly uint miniStreamCutoff_;
    private readonly uint[] fat_;
    private readonly uint[] miniFat_;
    private readonly byte[] miniStream_;
    private readonly Dictionary<string, DirectoryEntry> entries_;

    private CompoundBinaryFile(
        byte[] data,
        int sectorSize,
        int miniSectorSize,
        uint miniStreamCutoff,
        uint[] fat,
        uint[] miniFat,
        byte[] miniStream,
        Dictionary<string, DirectoryEntry> entries)
    {
        data_ = data;
        sectorSize_ = sectorSize;
        miniSectorSize_ = miniSectorSize;
        miniStreamCutoff_ = miniStreamCutoff;
        fat_ = fat;
        miniFat_ = miniFat;
        miniStream_ = miniStream;
        entries_ = entries;
    }

    /// <summary>
    /// Открывает CFB-файл и загружает служебные таблицы контейнера.
    /// </summary>
    public static CompoundBinaryFile Open(string path)
    {
        var data = File.ReadAllBytes(path);

        if (data.Length < 512)
        {
            throw new InvalidDataException("DOC/CFB-файл меньше обязательного заголовка.");
        }

        ReadOnlySpan<byte> signature = stackalloc byte[]
        {
            0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1
        };

        if (!data.AsSpan(0, 8).SequenceEqual(signature))
        {
            throw new InvalidDataException("Файл не является Microsoft Compound File Binary.");
        }

        var sectorShift = ReadUInt16(data, 0x1E);
        var miniSectorShift = ReadUInt16(data, 0x20);
        var sectorSize = 1 << sectorShift;
        var miniSectorSize = 1 << miniSectorShift;

        if (sectorSize is not (512 or 4096))
        {
            throw new InvalidDataException($"Неподдерживаемый размер CFB-сектора: {sectorSize}.");
        }

        if (miniSectorSize != 64)
        {
            throw new InvalidDataException($"Неподдерживаемый размер CFB mini-sector: {miniSectorSize}.");
        }

        var numberOfFatSectors = ReadUInt32(data, 0x2C);
        var firstDirectorySector = ReadUInt32(data, 0x30);
        var miniStreamCutoff = ReadUInt32(data, 0x38);
        var firstMiniFatSector = ReadUInt32(data, 0x3C);
        var numberOfMiniFatSectors = ReadUInt32(data, 0x40);
        var firstDifatSector = ReadUInt32(data, 0x44);
        var numberOfDifatSectors = ReadUInt32(data, 0x48);

        var difat = ReadDifat(
            data,
            sectorSize,
            numberOfFatSectors,
            firstDifatSector,
            numberOfDifatSectors);

        var fat = ReadFat(data, sectorSize, difat);
        var directoryBytes = ReadRegularStream(
            data,
            sectorSize,
            fat,
            firstDirectorySector,
            null);

        var entries = ReadDirectory(directoryBytes);
        var root = entries.Values.FirstOrDefault(entry => entry.ObjectType == 5)
                   ?? throw new InvalidDataException("CFB не содержит Root Entry.");

        var miniStream = root.StreamSize == 0
            ? Array.Empty<byte>()
            : ReadRegularStream(
                data,
                sectorSize,
                fat,
                root.StartSector,
                checked((long)root.StreamSize));

        var miniFat = numberOfMiniFatSectors == 0 ||
                      firstMiniFatSector is EndOfChain or FreeSector
            ? Array.Empty<uint>()
            : ReadMiniFat(
                data,
                sectorSize,
                fat,
                firstMiniFatSector,
                numberOfMiniFatSectors);

        return new CompoundBinaryFile(
            data,
            sectorSize,
            miniSectorSize,
            miniStreamCutoff,
            fat,
            miniFat,
            miniStream,
            entries);
    }

    /// <summary>
    /// Возвращает именованный поток CFB-контейнера.
    /// </summary>
    public byte[] ReadStream(string name)
    {
        if (!entries_.TryGetValue(name, out var entry) || entry.ObjectType != 2)
        {
            throw new InvalidDataException($"CFB не содержит поток '{name}'.");
        }

        if (entry.StreamSize == 0)
        {
            return Array.Empty<byte>();
        }

        if (entry.StreamSize < miniStreamCutoff_)
        {
            return ReadMiniStream(entry);
        }

        return ReadRegularStream(
            data_,
            sectorSize_,
            fat_,
            entry.StartSector,
            checked((long)entry.StreamSize));
    }

    /// <summary>
    /// Возвращает true, если контейнер содержит именованный поток.
    /// </summary>
    public bool ContainsStream(string name)
    {
        return entries_.TryGetValue(name, out var entry) && entry.ObjectType == 2;
    }

    private byte[] ReadMiniStream(DirectoryEntry entry)
    {
        if (miniFat_.Length == 0 || miniStream_.Length == 0)
        {
            throw new InvalidDataException(
                $"CFB-поток '{entry.Name}' требует MiniFAT, но MiniFAT отсутствует.");
        }

        using var output = new MemoryStream();
        var sector = entry.StartSector;
        var visited = new HashSet<uint>();

        while (sector != EndOfChain)
        {
            if (sector >= miniFat_.Length)
            {
                throw new InvalidDataException("MiniFAT содержит недопустимый номер сектора.");
            }

            if (!visited.Add(sector))
            {
                throw new InvalidDataException("Обнаружен цикл в MiniFAT.");
            }

            var offset = checked((long)sector * miniSectorSize_);
            if (offset < 0 || offset + miniSectorSize_ > miniStream_.LongLength)
            {
                throw new InvalidDataException("Mini-sector выходит за границы root mini stream.");
            }

            output.Write(miniStream_, checked((int)offset), miniSectorSize_);
            sector = miniFat_[sector];
        }

        return TrimToSize(output.ToArray(), entry.StreamSize);
    }

    private static uint[] ReadDifat(
        byte[] data,
        int sectorSize,
        uint numberOfFatSectors,
        uint firstDifatSector,
        uint numberOfDifatSectors)
    {
        var result = new List<uint>();

        for (var index = 0; index < 109; index++)
        {
            var sector = ReadUInt32(data, 0x4C + index * 4);
            if (sector != FreeSector)
            {
                result.Add(sector);
            }
        }

        var current = firstDifatSector;
        var visited = new HashSet<uint>();

        for (var index = 0u; index < numberOfDifatSectors; index++)
        {
            if (current is EndOfChain or FreeSector)
            {
                break;
            }

            if (!visited.Add(current))
            {
                throw new InvalidDataException("Обнаружен цикл в DIFAT.");
            }

            var offset = SectorOffset(current, sectorSize, data.Length);
            var entriesPerSector = sectorSize / 4 - 1;

            for (var item = 0; item < entriesPerSector; item++)
            {
                var sector = ReadUInt32(data, offset + item * 4);
                if (sector != FreeSector)
                {
                    result.Add(sector);
                }
            }

            current = ReadUInt32(data, offset + entriesPerSector * 4);
        }

        if (result.Count < numberOfFatSectors)
        {
            throw new InvalidDataException("CFB DIFAT содержит меньше FAT-секторов, чем заявлено.");
        }

        return result.Take(checked((int)numberOfFatSectors)).ToArray();
    }

    private static uint[] ReadFat(byte[] data, int sectorSize, IReadOnlyList<uint> difat)
    {
        var result = new List<uint>(difat.Count * (sectorSize / 4));

        foreach (var fatSector in difat)
        {
            if (fatSector is FreeSector or EndOfChain or FatSector or DifatSector)
            {
                throw new InvalidDataException("DIFAT содержит недопустимый FAT sector id.");
            }

            var offset = SectorOffset(fatSector, sectorSize, data.Length);

            for (var position = 0; position < sectorSize; position += 4)
            {
                result.Add(ReadUInt32(data, offset + position));
            }
        }

        return result.ToArray();
    }

    private static uint[] ReadMiniFat(
        byte[] data,
        int sectorSize,
        uint[] fat,
        uint firstMiniFatSector,
        uint numberOfMiniFatSectors)
    {
        var bytes = ReadRegularStream(
            data,
            sectorSize,
            fat,
            firstMiniFatSector,
            checked((long)numberOfMiniFatSectors * sectorSize));

        var result = new uint[bytes.Length / 4];

        for (var index = 0; index < result.Length; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(index * 4, 4));
        }

        return result;
    }

    private static Dictionary<string, DirectoryEntry> ReadDirectory(byte[] bytes)
    {
        var result = new Dictionary<string, DirectoryEntry>(StringComparer.OrdinalIgnoreCase);

        for (var offset = 0; offset + 128 <= bytes.Length; offset += 128)
        {
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                bytes.AsSpan(offset + 64, 2));

            if (nameLength < 2 || nameLength > 64 || (nameLength & 1) != 0)
            {
                continue;
            }

            var nameBytes = bytes.AsSpan(offset, nameLength - 2);
            var name = Encoding.Unicode.GetString(nameBytes);
            var objectType = bytes[offset + 66];

            if (objectType == 0 || string.IsNullOrEmpty(name))
            {
                continue;
            }

            var startSector = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(offset + 116, 4));

            var streamSize = BinaryPrimitives.ReadUInt64LittleEndian(
                bytes.AsSpan(offset + 120, 8));

            result[name] = new DirectoryEntry(
                name,
                objectType,
                startSector,
                streamSize);
        }

        return result;
    }

    private static byte[] ReadRegularStream(
        byte[] data,
        int sectorSize,
        uint[] fat,
        uint startSector,
        long? expectedSize)
    {
        if (startSector is EndOfChain or FreeSector)
        {
            return Array.Empty<byte>();
        }

        using var output = new MemoryStream();
        var current = startSector;
        var visited = new HashSet<uint>();

        while (current != EndOfChain)
        {
            if (current >= fat.Length)
            {
                throw new InvalidDataException("FAT содержит недопустимый номер сектора.");
            }

            if (!visited.Add(current))
            {
                throw new InvalidDataException("Обнаружен цикл в FAT.");
            }

            var offset = SectorOffset(current, sectorSize, data.Length);
            output.Write(data, offset, sectorSize);

            current = fat[current];

            if (expectedSize.HasValue && output.Length >= expectedSize.Value)
            {
                break;
            }
        }

        var bytes = output.ToArray();

        return expectedSize.HasValue
            ? TrimToSize(bytes, checked((ulong)expectedSize.Value))
            : bytes;
    }

    private static byte[] TrimToSize(byte[] bytes, ulong size)
    {
        if (size > int.MaxValue)
        {
            throw new InvalidDataException("CFB-поток слишком велик для текущего reader.");
        }

        var length = checked((int)size);

        if (bytes.Length < length)
        {
            throw new InvalidDataException("CFB-цепочка короче заявленного размера потока.");
        }

        if (bytes.Length == length)
        {
            return bytes;
        }

        return bytes.AsSpan(0, length).ToArray();
    }

    private static int SectorOffset(uint sector, int sectorSize, int fileLength)
    {
        var offset = checked(((long)sector + 1) * sectorSize);

        if (offset < 0 || offset + sectorSize > fileLength)
        {
            throw new InvalidDataException("CFB sector выходит за границы файла.");
        }

        return checked((int)offset);
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        if (offset < 0 || offset + 2 > data.Length)
        {
            throw new InvalidDataException("Чтение UInt16 выходит за границы CFB.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            throw new InvalidDataException("Чтение UInt32 выходит за границы CFB.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private sealed record DirectoryEntry(
        string Name,
        byte ObjectType,
        uint StartSector,
        ulong StreamSize);
}
