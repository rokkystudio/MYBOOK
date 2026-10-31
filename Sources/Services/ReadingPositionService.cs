using System;
using System.Collections.Generic;
using System.IO;

namespace MYBOOK.Services;

/// <summary>
/// Хранит координату вертикальной прокрутки в CSS-пикселях и полный
/// нормализованный путь документа. Координата принадлежит конкретному
/// файлу и не содержит HTML, DOM identifiers или исходного текста.
/// </summary>
internal sealed record DocumentReadingPosition
{
    public required string DocumentPath { get; init; }
    public required int ScrollTopPixels { get; init; }
}

/// <summary>
/// Управляет ограниченной историей позиций чтения по полному пути файла.
/// Сравнение путей выполняется по Windows case-insensitive semantics,
/// а координаты сохраняются без расчётных замен и интерполяции.
/// </summary>
internal static class ReadingPositionService
{
    public const int MaximumPositions = 200;

    /// <summary>
    /// Возвращает сохранённую координату для полного пути файла.
    /// Отсутствующая запись означает начало документа (0 CSS pixels).
    /// </summary>
    public static int GetPosition(
        AppSettings settings,
        string documentPath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var path =
            ValidateDocumentPath(documentPath);
        var positions = settings.ReadingPositions
            ?? throw new InvalidDataException(
                "Список позиций чтения отсутствует.");

        foreach (var position in positions)
        {
            if (string.Equals(
                position.DocumentPath,
                path,
                StringComparison.OrdinalIgnoreCase))
            {
                return position.ScrollTopPixels;
            }
        }

        return 0;
    }

    /// <summary>
    /// Сохраняет целочисленный vertical scroll offset документа, переносит
    /// обновлённую запись в начало истории и ограничивает её 200 файлами.
    /// Возвращает true только когда settings действительно изменяются.
    /// Начальная нулевая позиция не создаёт отдельной записи.
    /// </summary>
    public static bool RecordPosition(
        AppSettings settings,
        string documentPath,
        int scrollTopPixels)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var path =
            ValidateDocumentPath(documentPath);
        ValidateScrollTop(scrollTopPixels);

        var positions = settings.ReadingPositions
            ?? throw new InvalidDataException(
                "Список позиций чтения отсутствует.");

        var existingIndex = positions.FindIndex(
            position => string.Equals(
                position.DocumentPath,
                path,
                StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0 &&
            positions[existingIndex].ScrollTopPixels ==
                scrollTopPixels)
        {
            return false;
        }

        if (existingIndex < 0 &&
            scrollTopPixels == 0)
        {
            return false;
        }

        if (existingIndex >= 0)
        {
            positions.RemoveAt(existingIndex);
        }

        positions.Insert(
            0,
            new DocumentReadingPosition
            {
                DocumentPath = path,
                ScrollTopPixels = scrollTopPixels
            });

        if (positions.Count > MaximumPositions)
        {
            positions.RemoveRange(
                MaximumPositions,
                positions.Count - MaximumPositions);
        }

        return true;
    }

    /// <summary>
    /// Проверяет сохранённую историю: максимум 200 записей, нормализованные
    /// абсолютные пути, единственность пути без учёта регистра и координаты
    /// в диапазоне 0..Int32.MaxValue. Ошибочный JSON отвергается явно.
    /// </summary>
    public static void Validate(
        IReadOnlyList<DocumentReadingPosition>? positions)
    {
        if (positions == null ||
            positions.Count > MaximumPositions)
        {
            throw new InvalidDataException(
                "Список позиций чтения отсутствует или превышает 200 записей.");
        }

        var paths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var entry in positions)
        {
            if (entry == null)
            {
                throw new InvalidDataException(
                    "Список позиций чтения содержит null.");
            }

            var path =
                ValidateDocumentPath(
                    entry.DocumentPath);

            if (!string.Equals(
                path,
                entry.DocumentPath,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Путь позиции чтения не нормализован.");
            }

            ValidateScrollTop(
                entry.ScrollTopPixels);

            if (!paths.Add(path))
            {
                throw new InvalidDataException(
                    "Список позиций чтения содержит повторяющийся путь.");
            }
        }
    }

    /// <summary>
    /// Проверяет абсолютный путь локального файла и возвращает полный путь,
    /// нормализованный стандартными Windows filesystem rules.
    /// Относительные и пустые пути не принимаются.
    /// </summary>
    private static string ValidateDocumentPath(
        string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath) ||
            documentPath.Length > 32767)
        {
            throw new InvalidDataException(
                "Путь документа для позиции чтения должен быть абсолютным.");
        }

        try
        {
            if (!Path.IsPathFullyQualified(documentPath))
            {
                throw new InvalidDataException(
                    "Путь документа для позиции чтения должен быть абсолютным.");
            }

            return Path.GetFullPath(
                documentPath);
        }
        catch (Exception error)
            when (error is ArgumentException or
                 NotSupportedException or
                 PathTooLongException)
        {
            throw new InvalidDataException(
                "Путь документа для позиции чтения некорректен.",
                error);
        }
    }

    /// <summary>
    /// Проверяет неотрицательную CSS pixel координату, представимую в Int32.
    /// </summary>
    private static void ValidateScrollTop(
        int scrollTopPixels)
    {
        if (scrollTopPixels < 0)
        {
            throw new InvalidDataException(
                "Позиция чтения должна быть неотрицательной координатой CSS pixels.");
        }
    }
}
