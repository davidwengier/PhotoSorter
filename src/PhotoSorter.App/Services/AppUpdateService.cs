using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace PhotoSorter.App.Services;

public sealed record DownloadedAppUpdate(string Version);

public interface IAppUpdateService
{
    Task<DownloadedAppUpdate?> DownloadLatestAsync(CancellationToken cancellationToken = default);

    bool ApplyAndRestart();
}

public sealed class AppUpdateService : IAppUpdateService
{
    private const string RepositoryUrl = "https://github.com/davidwengier/PhotoSorter";

    private readonly ILogger<AppUpdateService> _logger;
    private readonly UpdateManager _updateManager;
    private VelopackAsset? _readyRelease;

    public AppUpdateService(ILogger<AppUpdateService> logger)
    {
        _logger = logger;
        _updateManager = new UpdateManager(new GithubSource(RepositoryUrl, null, prerelease: false));
    }

    public async Task<DownloadedAppUpdate?> DownloadLatestAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_updateManager.IsInstalled)
        {
            _logger.LogDebug("Skipping update check because this is not a Velopack installation.");
            return null;
        }

        try
        {
            var pendingRelease = _updateManager.UpdatePendingRestart;
            if (pendingRelease is not null)
            {
                return RememberReadyRelease(pendingRelease);
            }

            var update = await _updateManager.CheckForUpdatesAsync();
            if (update is null)
            {
                _logger.LogDebug("PhotoSorter is up to date.");
                return null;
            }

            await _updateManager.DownloadUpdatesAsync(
                update,
                cancelToken: cancellationToken);
            return RememberReadyRelease(update.TargetFullRelease);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedDownloadFailure(exception))
        {
            _logger.LogWarning(exception, "PhotoSorter could not check for or download an update.");
            return null;
        }
    }

    public bool ApplyAndRestart()
    {
        if (_readyRelease is null)
        {
            _logger.LogWarning("An update restart was requested before an update was downloaded.");
            return false;
        }

        try
        {
            _updateManager.ApplyUpdatesAndRestart(_readyRelease);
            return true;
        }
        catch (Exception exception) when (IsExpectedApplyFailure(exception))
        {
            _logger.LogError(exception, "PhotoSorter could not apply the downloaded update.");
            return false;
        }
    }

    private DownloadedAppUpdate RememberReadyRelease(VelopackAsset release)
    {
        _readyRelease = release;
        _logger.LogInformation(
            "PhotoSorter update {Version} is downloaded and ready to install.",
            release.Version);
        return new DownloadedAppUpdate(release.Version.ToString());
    }

    private static bool IsExpectedDownloadFailure(Exception exception) =>
        exception is NotInstalledException
            or AcquireLockFailedException
            or ChecksumFailedException
            or HttpRequestException
            or TaskCanceledException
            or IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException;

    private static bool IsExpectedApplyFailure(Exception exception) =>
        exception is NotInstalledException
            or AcquireLockFailedException
            or IOException
            or UnauthorizedAccessException
            or Win32Exception
            or InvalidOperationException;
}
