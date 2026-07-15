using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoSorter.App.Services;
using PhotoSorter.App.ViewModels;
using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Services;
using PhotoSorter.Infrastructure.Cache;
using PhotoSorter.Infrastructure.Geocoding;
using PhotoSorter.Infrastructure.Logging;
using PhotoSorter.Infrastructure.Media;
using PhotoSorter.Infrastructure.Moves;
using PhotoSorter.Infrastructure.State;
using PhotoSorter.Infrastructure.Thumbnails;

namespace PhotoSorter.App;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();
        var cachePathProvider = new CachePathProvider();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LocalFileLoggerProvider(cachePathProvider.BasePath));

        builder.Services.AddSingleton(cachePathProvider);
        builder.Services.AddSingleton<ISharedStateStore, JsonSharedStateStore>();
        builder.Services.AddSingleton<IMediaCacheFactory, SqliteMediaCacheFactory>();
        builder.Services.AddSingleton<IRecentRootStore, RecentRootStore>();
        builder.Services.AddSingleton<PreviewWindowPlacementStore>();
        builder.Services.AddSingleton<AssetBundler>();
        builder.Services.AddSingleton<DecisionMatcher>();
        builder.Services.AddSingleton<GroupingEngine>();
        builder.Services.AddSingleton<CandidateEditor>();
        builder.Services.AddSingleton<MovePlanner>();
        builder.Services.AddSingleton<MediaMetadataReader>();
        builder.Services.AddSingleton<IMediaScanner, MediaScanner>();
        builder.Services.AddSingleton<IThumbnailService, WindowsThumbnailService>();
        builder.Services.AddSingleton<IMoveExecutor, FileSystemMoveExecutor>();
        builder.Services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        });
        builder.Services.AddSingleton<IReverseGeocoder, NominatimReverseGeocoder>();
        builder.Services.AddSingleton<IUserDialogService, UserDialogService>();
        builder.Services.AddSingleton<IAppUpdateService, AppUpdateService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();
        Services = _host.Services;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        Services.GetRequiredService<ILogger<App>>()
            .LogCritical(eventArgs.Exception, "Unhandled UI exception.");
        MessageBox.Show(
            eventArgs.Exception.Message,
            "Unexpected PhotoSorter error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        eventArgs.Handled = true;
        Shutdown(-1);
    }
}
