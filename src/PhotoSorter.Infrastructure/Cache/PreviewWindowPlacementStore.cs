using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PhotoSorter.Infrastructure.Cache;

public sealed class PreviewWindowPlacementStore
{
    private const string FileName = "preview-window.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly ILogger<PreviewWindowPlacementStore> _logger;

    public PreviewWindowPlacementStore(
        CachePathProvider pathProvider,
        ILogger<PreviewWindowPlacementStore> logger)
        : this(pathProvider.BasePath, logger)
    {
    }

    public PreviewWindowPlacementStore(
        string cacheBasePath,
        ILogger<PreviewWindowPlacementStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheBasePath);
        _path = Path.Combine(cacheBasePath, FileName);
        _logger = logger;
    }

    public PreviewWindowPlacement? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var placement = JsonSerializer.Deserialize<PreviewWindowPlacement>(stream, JsonOptions);
            if (placement?.IsValid is not true)
            {
                _logger.LogWarning("Ignoring invalid photo preview window placement at {Path}.", _path);
                return null;
            }

            return placement;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not read photo preview window placement from {Path}.", _path);
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Could not read photo preview window placement from {Path}.", _path);
            return null;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Ignoring malformed photo preview window placement at {Path}.", _path);
            return null;
        }
    }

    public void Save(PreviewWindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!placement.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(placement), "Window placement must contain finite, positive bounds.");
        }

        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4_096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, placement, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Could not save photo preview window placement to {Path}.", _path);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Could not save photo preview window placement to {Path}.", _path);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private void DeleteTemporaryFile(string? temporaryPath)
    {
        if (temporaryPath is null || !File.Exists(temporaryPath))
        {
            return;
        }

        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException exception)
        {
            _logger.LogDebug(exception, "Could not delete temporary window placement file {Path}.", temporaryPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogDebug(exception, "Could not delete temporary window placement file {Path}.", temporaryPath);
        }
    }
}

public sealed record PreviewWindowPlacement
{
    public double Left { get; init; }

    public double Top { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public bool IsMaximized { get; init; }

    public bool IsValid =>
        double.IsFinite(Left)
        && double.IsFinite(Top)
        && double.IsFinite(Width)
        && double.IsFinite(Height)
        && Width > 0
        && Height > 0;
}
