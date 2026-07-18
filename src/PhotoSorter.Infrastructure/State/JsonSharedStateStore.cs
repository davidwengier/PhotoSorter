using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Infrastructure.State;

public sealed class JsonSharedStateStore : ISharedStateStore
{
    public const string FileName = ".photosorter.json";
    private const int LegacySchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string GetStatePath(string picturesRoot)
    {
        ValidateRoot(picturesRoot);
        return Path.Combine(Path.GetFullPath(picturesRoot), FileName);
    }

    public async Task<PhotoSorterState> LoadAsync(
        string picturesRoot,
        CancellationToken cancellationToken = default)
    {
        var statePath = GetStatePath(picturesRoot);
        if (!File.Exists(statePath))
        {
            return new PhotoSorterState();
        }

        var result = await ReadAsync(statePath, cancellationToken).ConfigureAwait(false);
        if (!result.RequiresMigration)
        {
            return result.State;
        }

        await using var stateLock = await AcquireLockAsync(statePath, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(statePath))
        {
            return new PhotoSorterState();
        }

        result = await ReadAsync(statePath, cancellationToken).ConfigureAwait(false);
        if (!result.RequiresMigration)
        {
            return result.State;
        }

        if (HasDecisions(result.State))
        {
            await WriteAsync(statePath, result.State, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            DeleteStateFile(statePath);
        }

        return result.State;
    }

    public async Task<PhotoSorterState> UpdateAsync(
        string picturesRoot,
        Func<PhotoSorterState, PhotoSorterState> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var statePath = GetStatePath(picturesRoot);
        await using var stateLock = await AcquireLockAsync(statePath, cancellationToken).ConfigureAwait(false);
        var current = File.Exists(statePath)
            ? (await ReadAsync(statePath, cancellationToken).ConfigureAwait(false)).State
            : new PhotoSorterState();
        var updated = update(current)
            ?? throw new StateFileException("The state update returned no state.");
        ValidateState(updated, statePath);
        if (!HasDecisions(updated))
        {
            DeleteStateFile(statePath);
            return updated;
        }

        await WriteAsync(statePath, updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private static async Task<StateReadResult> ReadAsync(
        string statePath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var root = await JsonNode.ParseAsync(
                stream,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (root is not JsonObject stateObject)
            {
                throw new StateFileException($"'{statePath}' does not contain a JSON object.");
            }

            var schemaVersion = ReadSchemaVersion(stateObject);
            var requiresMigration = schemaVersion == LegacySchemaVersion;
            if (requiresMigration)
            {
                MigrateVersion1(stateObject);
            }
            else if (schemaVersion != PhotoSorterState.CurrentSchemaVersion)
            {
                throw new StateFileException(
                    $"'{statePath}' uses unsupported schemaVersion {schemaVersion}; "
                    + $"supported versions are {LegacySchemaVersion} and {PhotoSorterState.CurrentSchemaVersion}.");
            }

            var state = stateObject.Deserialize<PhotoSorterState>(SerializerOptions)
                ?? throw new StateFileException($"'{statePath}' does not contain a JSON object.");
            ValidateState(state, statePath);
            return new StateReadResult(state, requiresMigration);
        }
        catch (StateFileException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new StateFileException(
                $"Could not read '{statePath}' as PhotoSorter JSON at line "
                + $"{exception.LineNumber}, byte {exception.BytePositionInLine}: {exception.Message}",
                exception);
        }
        catch (IOException exception)
        {
            throw new StateFileException($"Could not read '{statePath}': {exception.Message}", exception);
        }
    }

    private static async Task WriteAsync(
        string statePath,
        PhotoSorterState state,
        CancellationToken cancellationToken)
    {
        ValidateState(state, statePath);

        var temporaryPath = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(state, SerializerOptions) + Environment.NewLine;
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(statePath))
            {
                File.Replace(temporaryPath, statePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, statePath);
            }
        }
        catch (IOException exception)
        {
            throw new StateFileException($"Could not write '{statePath}': {exception.Message}", exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<FileStream> AcquireLockAsync(
        string statePath,
        CancellationToken cancellationToken)
    {
        var lockPath = statePath + ".lock";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new DeletingLockFileStream(lockPath);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                if (File.Exists(lockPath)
                    && DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(lockPath) > TimeSpan.FromMinutes(2))
                {
                    TryDelete(lockPath);
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new StateFileException(
                    $"Could not lock '{statePath}' for an update: {exception.Message}",
                    exception);
            }
        }
    }

    private static void ValidateState(PhotoSorterState state, string statePath)
    {
        var errors = PhotoSorterStateValidator.Validate(state);
        if (errors.Count > 0)
        {
            throw new StateFileException(
                $"'{statePath}' is not a valid PhotoSorter state file:{Environment.NewLine}- "
                + string.Join(Environment.NewLine + "- ", errors));
        }
    }

    private static bool HasDecisions(PhotoSorterState state) =>
        state.RoutineLocations.Count > 0
        || state.IgnoredGroups.Count > 0;

    private static int ReadSchemaVersion(JsonObject stateObject)
    {
        if (!stateObject.TryGetPropertyValue("schemaVersion", out var versionNode))
        {
            return LegacySchemaVersion;
        }

        if (versionNode is null)
        {
            throw new JsonException("schemaVersion cannot be null.");
        }

        return versionNode.Deserialize<int>(SerializerOptions);
    }

    private static void MigrateVersion1(JsonObject stateObject)
    {
        stateObject["schemaVersion"] = PhotoSorterState.CurrentSchemaVersion;
        stateObject.Remove("ignoredFolders");
        stateObject.Remove("preferences");

        if (stateObject["routineLocations"] is not JsonArray routineLocations)
        {
            return;
        }

        foreach (var node in routineLocations.ToArray())
        {
            if (node is not JsonObject routineLocation)
            {
                continue;
            }

            var disposition = ReadLegacyDisposition(routineLocation);
            var suppressCandidates = ReadLegacySuppression(routineLocation);
            routineLocation.Remove("disposition");
            routineLocation.Remove("suppressCandidates");
            if (disposition != LegacyRoutineLocationDisposition.Routine || !suppressCandidates)
            {
                routineLocations.Remove(node);
            }
        }
    }

    private static LegacyRoutineLocationDisposition ReadLegacyDisposition(JsonObject routineLocation)
    {
        if (!routineLocation.TryGetPropertyValue("disposition", out var dispositionNode))
        {
            return LegacyRoutineLocationDisposition.Routine;
        }

        if (dispositionNode is null)
        {
            throw new JsonException("Routine location disposition cannot be null.");
        }

        return dispositionNode.Deserialize<LegacyRoutineLocationDisposition>(SerializerOptions);
    }

    private static bool ReadLegacySuppression(JsonObject routineLocation)
    {
        if (!routineLocation.TryGetPropertyValue("suppressCandidates", out var suppressionNode))
        {
            return true;
        }

        if (suppressionNode is null)
        {
            throw new JsonException("Routine location suppressCandidates cannot be null.");
        }

        return suppressionNode.Deserialize<bool>(SerializerOptions);
    }

    private static void DeleteStateFile(string statePath)
    {
        try
        {
            File.Delete(statePath);
        }
        catch (IOException exception)
        {
            throw new StateFileException($"Could not delete empty '{statePath}': {exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new StateFileException($"Could not delete empty '{statePath}': {exception.Message}", exception);
        }
    }

    private static void ValidateRoot(string picturesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(picturesRoot);
        if (!Directory.Exists(picturesRoot))
        {
            throw new DirectoryNotFoundException($"Pictures folder '{picturesRoot}' does not exist.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class DeletingLockFileStream : FileStream
    {
        private readonly string _path;

        public DeletingLockFileStream(string path)
            : base(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
        {
            _path = path;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TryDelete(_path);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            TryDelete(_path);
            GC.SuppressFinalize(this);
        }
    }

    private sealed record StateReadResult(PhotoSorterState State, bool RequiresMigration);

    private enum LegacyRoutineLocationDisposition
    {
        Routine,
        NotRoutine,
    }
}
