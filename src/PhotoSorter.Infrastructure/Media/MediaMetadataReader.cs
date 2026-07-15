using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using PhotoSorter.Core.Models;
using MetadataDirectory = MetadataExtractor.Directory;

namespace PhotoSorter.Infrastructure.Media;

public sealed partial class MediaMetadataReader
{
    public MediaAsset Read(
        string absolutePath,
        string relativePath,
        int year,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        FileAttributes attributes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var extension = Path.GetExtension(relativePath).ToLowerInvariant();
        var kind = GetMediaKind(extension);
        if (kind == MediaKind.Sidecar)
        {
            return CreateFallbackAsset(
                relativePath,
                year,
                length,
                lastWriteTimeUtc,
                attributes,
                kind,
                extension,
                metadataError: null,
                metadataReadFailed: false);
        }

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(absolutePath);
            var timestamp = ReadTimestamp(directories, relativePath, lastWriteTimeUtc);
            var dimensions = ReadDimensions(directories);
            return new MediaAsset
            {
                RelativePath = relativePath,
                Year = year,
                Length = length,
                LastWriteTimeUtc = lastWriteTimeUtc,
                Attributes = attributes,
                Kind = kind,
                Extension = extension,
                CapturedAt = timestamp.Value,
                TimestampSource = timestamp.Source,
                TimestampConfidence = timestamp.Confidence,
                OffsetConfidence = timestamp.OffsetConfidence,
                Location = ReadLocation(directories),
                ContentIdentifier = ReadContentIdentifier(directories),
                Duration = ReadDuration(directories),
                Width = dimensions.Width,
                Height = dimensions.Height,
                MetadataError = ReadDirectoryErrors(directories),
                MetadataReadFailed = false,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CreateFallbackAsset(
                relativePath,
                year,
                length,
                lastWriteTimeUtc,
                attributes,
                kind,
                extension,
                exception.Message,
                metadataReadFailed: true);
        }
    }

    public static MediaKind GetMediaKind(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".heic" or ".heif" or ".png" or ".webp" or ".gif"
                or ".dng" or ".avif" or ".jp2" or ".bmp" or ".tif" or ".tiff" => MediaKind.Image,
            ".mov" or ".mp4" or ".m4v" or ".3gp" or ".avi" => MediaKind.Video,
            ".xmp" or ".nar" => MediaKind.Sidecar,
            _ => MediaKind.Unknown,
        };

    private static TimestampResult ReadTimestamp(
        IReadOnlyList<MetadataDirectory> directories,
        string relativePath,
        DateTimeOffset lastWriteTimeUtc)
    {
        foreach (var directory in directories.OfType<ExifSubIfdDirectory>())
        {
            if (directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime)
                || directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out dateTime))
            {
                var offsetText = GetString(directory, ExifDirectoryBase.TagTimeZoneOriginal)
                    ?? GetString(directory, ExifDirectoryBase.TagTimeZoneDigitized);
                return FromUnspecifiedDate(
                    dateTime,
                    offsetText,
                    TimestampSource.ExifOriginal,
                    MetadataConfidence.High);
            }
        }

        foreach (var directory in directories.OfType<ExifIfd0Directory>())
        {
            if (directory.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dateTime))
            {
                return FromUnspecifiedDate(
                    dateTime,
                    GetString(directory, ExifDirectoryBase.TagTimeZone),
                    TimestampSource.ExifOriginal,
                    MetadataConfidence.High);
            }
        }

        foreach (var directory in directories.OfType<QuickTimeMetadataHeaderDirectory>())
        {
            var creationDate = GetString(directory, QuickTimeMetadataHeaderDirectory.TagCreationDate);
            if (DateTimeOffset.TryParse(
                    creationDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var parsed))
            {
                return new TimestampResult(
                    parsed,
                    TimestampSource.MediaCreated,
                    MetadataConfidence.High,
                    HasExplicitOffset(creationDate)
                        ? OffsetConfidence.Explicit
                        : OffsetConfidence.Inferred);
            }
        }

        foreach (var directory in directories.OfType<QuickTimeMovieHeaderDirectory>())
        {
            if (directory.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var dateTime))
            {
                return dateTime.Kind == DateTimeKind.Utc
                    ? new TimestampResult(
                        new DateTimeOffset(dateTime),
                        TimestampSource.MediaCreated,
                        MetadataConfidence.High,
                        OffsetConfidence.Explicit)
                    : FromUnspecifiedDate(
                        dateTime,
                        offsetText: null,
                        TimestampSource.MediaCreated,
                        MetadataConfidence.High);
            }
        }

        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (TryReadFileNameTimestamp(fileName, out var fileNameTimestamp))
        {
            return new TimestampResult(
                fileNameTimestamp,
                TimestampSource.FileName,
                MetadataConfidence.Medium,
                OffsetConfidence.Inferred);
        }

        return new TimestampResult(
            lastWriteTimeUtc,
            TimestampSource.FileSystem,
            MetadataConfidence.Low,
            OffsetConfidence.Unknown);
    }

    private static TimestampResult FromUnspecifiedDate(
        DateTime dateTime,
        string? offsetText,
        TimestampSource source,
        MetadataConfidence confidence)
    {
        var unspecified = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        if (TryParseOffset(offsetText, out var offset))
        {
            return new TimestampResult(
                new DateTimeOffset(unspecified, offset),
                source,
                confidence,
                OffsetConfidence.Explicit);
        }

        var inferredOffset = TimeZoneInfo.Local.GetUtcOffset(unspecified);
        return new TimestampResult(
            new DateTimeOffset(unspecified, inferredOffset),
            source,
            confidence,
            OffsetConfidence.Inferred);
    }

    private static GeoPoint? ReadLocation(IReadOnlyList<MetadataDirectory> directories)
    {
        foreach (var gps in directories.OfType<GpsDirectory>())
        {
            if (gps.TryGetGeoLocation(out var location)
                && location.Latitude is >= -90 and <= 90
                && location.Longitude is >= -180 and <= 180)
            {
                return new GeoPoint(location.Latitude, location.Longitude);
            }
        }

        foreach (var directory in directories.OfType<QuickTimeMetadataHeaderDirectory>())
        {
            var value = GetString(directory, QuickTimeMetadataHeaderDirectory.TagGpsLocation);
            var match = QuickTimeLocationRegex().Match(value ?? string.Empty);
            if (match.Success
                && double.TryParse(
                    match.Groups["latitude"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var latitude)
                && double.TryParse(
                    match.Groups["longitude"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var longitude))
            {
                return new GeoPoint(latitude, longitude);
            }
        }

        return null;
    }

    private static string? ReadContentIdentifier(IReadOnlyList<MetadataDirectory> directories)
    {
        foreach (var directory in directories.OfType<QuickTimeMetadataHeaderDirectory>())
        {
            var identifier = GetString(directory, QuickTimeMetadataHeaderDirectory.TagContentIdentifier);
            if (!string.IsNullOrWhiteSpace(identifier))
            {
                return identifier.Trim();
            }
        }

        return directories
            .SelectMany(static directory => directory.Tags)
            .FirstOrDefault(
                static tag => tag.Name.Contains(
                    "Content Identifier",
                    StringComparison.OrdinalIgnoreCase))
            ?.Description
            ?.Trim();
    }

    private static TimeSpan? ReadDuration(IReadOnlyList<MetadataDirectory> directories)
    {
        foreach (var directory in directories.OfType<QuickTimeMovieHeaderDirectory>())
        {
            if (directory.TryGetInt64(QuickTimeMovieHeaderDirectory.TagDuration, out var duration)
                && directory.TryGetInt64(QuickTimeMovieHeaderDirectory.TagTimeScale, out var timeScale)
                && duration >= 0
                && timeScale > 0)
            {
                return TimeSpan.FromSeconds(duration / (double)timeScale);
            }
        }

        return null;
    }

    private static (int? Width, int? Height) ReadDimensions(
        IReadOnlyList<MetadataDirectory> directories)
    {
        int? width = null;
        int? height = null;
        foreach (var directory in directories)
        {
            foreach (var tag in directory.Tags)
            {
                if (width is null
                    && tag.Name.Contains("Width", StringComparison.OrdinalIgnoreCase)
                    && TryGetPositiveInteger(directory.GetObject(tag.Type), out var widthValue))
                {
                    width = widthValue;
                }

                if (height is null
                    && tag.Name.Contains("Height", StringComparison.OrdinalIgnoreCase)
                    && TryGetPositiveInteger(directory.GetObject(tag.Type), out var heightValue))
                {
                    height = heightValue;
                }
            }
        }

        return (width, height);
    }

    private static string? ReadDirectoryErrors(IReadOnlyList<MetadataDirectory> directories)
    {
        var errors = directories
            .SelectMany(static directory => directory.Errors)
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return errors.Length == 0 ? null : string.Join("; ", errors);
    }

    private static bool TryReadFileNameTimestamp(
        string fileName,
        out DateTimeOffset timestamp)
    {
        var compactMatch = CompactFileNameDateRegex().Match(fileName);
        var compactValue = compactMatch.Success
            ? compactMatch.Groups["value"].Value.Replace("_", string.Empty).Replace("-", string.Empty)
            : string.Empty;
        if (compactMatch.Success
            && DateTime.TryParseExact(
                compactValue,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var compactDate))
        {
            timestamp = WithLocalOffset(compactDate);
            return true;
        }

        var separatedMatch = SeparatedFileNameDateRegex().Match(fileName);
        if (separatedMatch.Success)
        {
            var normalized = separatedMatch.Groups["value"].Value
                .Replace('.', ':')
                .Replace('_', ' ');
            if (DateTime.TryParseExact(
                    normalized,
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var separatedDate))
            {
                timestamp = WithLocalOffset(separatedDate);
                return true;
            }
        }

        timestamp = default;
        return false;
    }

    private static DateTimeOffset WithLocalOffset(DateTime value)
    {
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZoneInfo.Local.GetUtcOffset(unspecified));
    }

    private static bool TryParseOffset(string? value, out TimeSpan offset)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var normalized = value.Trim();
            if (normalized.Length == 5 && normalized[0] is '+' or '-')
            {
                normalized = normalized.Insert(3, ":");
            }

            if (TimeSpan.TryParseExact(
                    normalized,
                    @"hh\:mm",
                    CultureInfo.InvariantCulture,
                    out offset))
            {
                return true;
            }

            if (normalized.StartsWith('-')
                && TimeSpan.TryParseExact(
                    normalized[1..],
                    @"hh\:mm",
                    CultureInfo.InvariantCulture,
                    out offset))
            {
                offset = -offset;
                return true;
            }

            if (normalized.StartsWith('+')
                && TimeSpan.TryParseExact(
                    normalized[1..],
                    @"hh\:mm",
                    CultureInfo.InvariantCulture,
                    out offset))
            {
                return true;
            }
        }

        offset = default;
        return false;
    }

    private static string? GetString(MetadataDirectory directory, int tagType) =>
        directory.Tags.Any(tag => tag.Type == tagType)
            ? directory.GetString(tagType)
            : null;

    private static bool TryGetPositiveInteger(object? value, out int result)
    {
        try
        {
            result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return result > 0;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static bool HasExplicitOffset(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && ExplicitOffsetRegex().IsMatch(value);

    private static MediaAsset CreateFallbackAsset(
        string relativePath,
        int year,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        FileAttributes attributes,
        MediaKind kind,
        string extension,
        string? metadataError,
        bool metadataReadFailed) => new()
        {
            RelativePath = relativePath,
            Year = year,
            Length = length,
            LastWriteTimeUtc = lastWriteTimeUtc,
            Attributes = attributes,
            Kind = kind,
            Extension = extension,
            CapturedAt = lastWriteTimeUtc,
            TimestampSource = TimestampSource.FileSystem,
            TimestampConfidence = MetadataConfidence.Low,
            OffsetConfidence = OffsetConfidence.Unknown,
            MetadataError = metadataError,
            MetadataReadFailed = metadataReadFailed,
        };

    private readonly record struct TimestampResult(
        DateTimeOffset Value,
        TimestampSource Source,
        MetadataConfidence Confidence,
        OffsetConfidence OffsetConfidence);

    [GeneratedRegex(@"(?<latitude>[+-]\d{2}(?:\.\d+)?)(?<longitude>[+-]\d{3}(?:\.\d+)?)")]
    private static partial Regex QuickTimeLocationRegex();

    [GeneratedRegex(@"(?<!\d)(?<value>\d{8}[_-]?\d{6})(?!\d)")]
    private static partial Regex CompactFileNameDateRegex();

    [GeneratedRegex(@"(?<!\d)(?<value>\d{4}-\d{2}-\d{2}[ _]\d{2}[.-]\d{2}[.-]\d{2})(?!\d)")]
    private static partial Regex SeparatedFileNameDateRegex();

    [GeneratedRegex(@"(?:Z|[+-]\d{2}:?\d{2})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffsetRegex();
}
