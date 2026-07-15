using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Infrastructure.Cache;

public sealed class SqliteMediaCache(string databasePath) : IMediaCache
{
    private const int CurrentSchemaVersion = 2;
    private const string DateFormat = "O";

    private readonly string _databasePath = databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(
            await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The disposable cache schema version {version} is newer than the supported "
                + $"version {CurrentSchemaVersion}. Delete '{_databasePath}' to rebuild it.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = (version switch
        {
            0 =>
                """
                    DROP TABLE IF EXISTS media_assets;
                    DROP TABLE IF EXISTS geocode_cache;

                    """,
            1 =>
                """
                    DROP TABLE IF EXISTS media_assets;

                    """,
            _ => string.Empty,
        })
            +
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS media_assets (
                relative_path TEXT PRIMARY KEY COLLATE NOCASE,
                year INTEGER NOT NULL,
                length INTEGER NOT NULL,
                last_write_utc TEXT NOT NULL,
                attributes INTEGER NOT NULL,
                media_kind INTEGER NOT NULL,
                extension TEXT NOT NULL,
                captured_at TEXT NOT NULL,
                timestamp_source INTEGER NOT NULL,
                timestamp_confidence INTEGER NOT NULL,
                offset_confidence INTEGER NOT NULL,
                latitude REAL NULL,
                longitude REAL NULL,
                content_identifier TEXT NULL,
                duration_seconds REAL NULL,
                width INTEGER NULL,
                height INTEGER NULL,
                metadata_error TEXT NULL,
                metadata_read_failed INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS geocode_cache (
                cache_key TEXT PRIMARY KEY,
                display_name TEXT NOT NULL
            );

            PRAGMA user_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, MediaAsset>> LoadAssetsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT relative_path, year, length, last_write_utc, attributes, media_kind,
                   extension, captured_at, timestamp_source, timestamp_confidence,
                   offset_confidence, latitude, longitude, content_identifier, duration_seconds,
                   width, height, metadata_error, metadata_read_failed
            FROM media_assets;
            """;

        var assets = new Dictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var latitude = reader.IsDBNull(11) ? (double?)null : reader.GetDouble(11);
            var longitude = reader.IsDBNull(12) ? (double?)null : reader.GetDouble(12);
            var asset = new MediaAsset
            {
                RelativePath = reader.GetString(0),
                Year = reader.GetInt32(1),
                Length = reader.GetInt64(2),
                LastWriteTimeUtc = DateTimeOffset.ParseExact(
                    reader.GetString(3),
                    DateFormat,
                    CultureInfo.InvariantCulture),
                Attributes = (FileAttributes)reader.GetInt64(4),
                Kind = (MediaKind)reader.GetInt32(5),
                Extension = reader.GetString(6),
                CapturedAt = DateTimeOffset.ParseExact(
                    reader.GetString(7),
                    DateFormat,
                    CultureInfo.InvariantCulture),
                TimestampSource = (TimestampSource)reader.GetInt32(8),
                TimestampConfidence = (MetadataConfidence)reader.GetInt32(9),
                OffsetConfidence = (OffsetConfidence)reader.GetInt32(10),
                Location = latitude is not null && longitude is not null
                    ? new GeoPoint(latitude.Value, longitude.Value)
                    : null,
                ContentIdentifier = reader.IsDBNull(13) ? null : reader.GetString(13),
                Duration = reader.IsDBNull(14)
                    ? null
                    : TimeSpan.FromSeconds(reader.GetDouble(14)),
                Width = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                Height = reader.IsDBNull(16) ? null : reader.GetInt32(16),
                MetadataError = reader.IsDBNull(17) ? null : reader.GetString(17),
                MetadataReadFailed = reader.GetBoolean(18),
            };
            assets[asset.RelativePath] = asset;
        }

        return assets;
    }

    public async Task ReplaceAssetsAsync(
        IReadOnlyCollection<MediaAsset> assets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assets);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        await using (var setup = connection.CreateCommand())
        {
            setup.Transaction = transaction;
            setup.CommandText =
                """
                CREATE TEMP TABLE IF NOT EXISTS seen_paths (
                    relative_path TEXT PRIMARY KEY COLLATE NOCASE
                );
                DELETE FROM seen_paths;
                """;
            await setup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var upsert = CreateUpsertCommand(connection, transaction);
        await using var markSeen = connection.CreateCommand();
        markSeen.Transaction = transaction;
        markSeen.CommandText = "INSERT OR IGNORE INTO seen_paths(relative_path) VALUES ($relative_path);";
        var seenPathParameter = markSeen.Parameters.Add("$relative_path", SqliteType.Text);

        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetParameters(upsert, asset);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            seenPathParameter.Value = asset.RelativePath;
            await markSeen.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteMissing = connection.CreateCommand())
        {
            deleteMissing.Transaction = transaction;
            deleteMissing.CommandText =
                """
                DELETE FROM media_assets
                WHERE relative_path NOT IN (SELECT relative_path FROM seen_paths);
                """;
            await deleteMissing.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetGeocodeAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT display_name FROM geocode_cache WHERE cache_key = $key;";
        command.Parameters.AddWithValue("$key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    public async Task SetGeocodeAsync(
        string key,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO geocode_cache(cache_key, display_name)
            VALUES ($key, $display_name)
            ON CONFLICT(cache_key) DO UPDATE SET display_name = excluded.display_name;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$display_name", displayName);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static SqliteCommand CreateUpsertCommand(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO media_assets (
                relative_path, year, length, last_write_utc, attributes, media_kind,
                extension, captured_at, timestamp_source, timestamp_confidence,
                offset_confidence, latitude, longitude, content_identifier, duration_seconds,
                width, height, metadata_error, metadata_read_failed)
            VALUES (
                $relative_path, $year, $length, $last_write_utc, $attributes, $media_kind,
                $extension, $captured_at, $timestamp_source, $timestamp_confidence,
                $offset_confidence, $latitude, $longitude, $content_identifier, $duration_seconds,
                $width, $height, $metadata_error, $metadata_read_failed)
            ON CONFLICT(relative_path) DO UPDATE SET
                year = excluded.year,
                length = excluded.length,
                last_write_utc = excluded.last_write_utc,
                attributes = excluded.attributes,
                media_kind = excluded.media_kind,
                extension = excluded.extension,
                captured_at = excluded.captured_at,
                timestamp_source = excluded.timestamp_source,
                timestamp_confidence = excluded.timestamp_confidence,
                offset_confidence = excluded.offset_confidence,
                latitude = excluded.latitude,
                longitude = excluded.longitude,
                content_identifier = excluded.content_identifier,
                duration_seconds = excluded.duration_seconds,
                width = excluded.width,
                height = excluded.height,
                metadata_error = excluded.metadata_error,
                metadata_read_failed = excluded.metadata_read_failed;
            """;
        foreach (var name in new[]
                 {
                     "$relative_path",
                     "$year",
                     "$length",
                     "$last_write_utc",
                     "$attributes",
                     "$media_kind",
                     "$extension",
                     "$captured_at",
                     "$timestamp_source",
                     "$timestamp_confidence",
                     "$offset_confidence",
                     "$latitude",
                     "$longitude",
                     "$content_identifier",
                     "$duration_seconds",
                     "$width",
                     "$height",
                     "$metadata_error",
                     "$metadata_read_failed",
                 })
        {
            command.Parameters.Add(new SqliteParameter(name, null));
        }

        return command;
    }

    private static void SetParameters(SqliteCommand command, MediaAsset asset)
    {
        command.Parameters["$relative_path"].Value = asset.RelativePath;
        command.Parameters["$year"].Value = asset.Year;
        command.Parameters["$length"].Value = asset.Length;
        command.Parameters["$last_write_utc"].Value = asset.LastWriteTimeUtc.ToString(DateFormat, CultureInfo.InvariantCulture);
        command.Parameters["$attributes"].Value = (long)asset.Attributes;
        command.Parameters["$media_kind"].Value = (int)asset.Kind;
        command.Parameters["$extension"].Value = asset.Extension;
        command.Parameters["$captured_at"].Value = asset.CapturedAt.ToString(DateFormat, CultureInfo.InvariantCulture);
        command.Parameters["$timestamp_source"].Value = (int)asset.TimestampSource;
        command.Parameters["$timestamp_confidence"].Value = (int)asset.TimestampConfidence;
        command.Parameters["$offset_confidence"].Value = (int)asset.OffsetConfidence;
        command.Parameters["$latitude"].Value = asset.Location?.Latitude ?? (object)DBNull.Value;
        command.Parameters["$longitude"].Value = asset.Location?.Longitude ?? (object)DBNull.Value;
        command.Parameters["$content_identifier"].Value = asset.ContentIdentifier ?? (object)DBNull.Value;
        command.Parameters["$duration_seconds"].Value = asset.Duration?.TotalSeconds ?? (object)DBNull.Value;
        command.Parameters["$width"].Value = asset.Width ?? (object)DBNull.Value;
        command.Parameters["$height"].Value = asset.Height ?? (object)DBNull.Value;
        command.Parameters["$metadata_error"].Value = asset.MetadataError ?? (object)DBNull.Value;
        command.Parameters["$metadata_read_failed"].Value = asset.MetadataReadFailed;
    }
}
