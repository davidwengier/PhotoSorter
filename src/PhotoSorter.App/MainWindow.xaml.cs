using PhotoSorter.App.ViewModels;
using PhotoSorter.Infrastructure.Cache;

namespace PhotoSorter.App;

public partial class MainWindow : Window
{
    private readonly PreviewWindowPlacementStore _previewWindowPlacementStore;
    private bool _initialized;

    public MainWindow(
        MainViewModel viewModel,
        PreviewWindowPlacementStore previewWindowPlacementStore)
    {
        _previewWindowPlacementStore = previewWindowPlacementStore;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var viewModel = (MainViewModel)DataContext;
        await Task.WhenAll(
            viewModel.InitializeAsync(),
            viewModel.CheckForUpdatesAsync());
    }

    private void OnPhotoPreviewClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: BundleViewModel bundle })
        {
            return;
        }

        var viewModel = (MainViewModel)DataContext;
        viewModel.SelectedBundle = bundle;
        var window = new PhotoPreviewWindow(
            viewModel.SelectedBundles.ToArray(),
            bundle,
            _previewWindowPlacementStore)
        {
            Owner = this,
        };
        window.ShowDialog();
    }

    private void OnMoreButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { ContextMenu: not null } button)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }
}
