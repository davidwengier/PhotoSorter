using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
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

    private void OnCandidateSelectionChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        ScrollToTopAfterLayout(PhotoList, PhotoGrid);

    private void OnYearFilterSelectionChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        ScrollToTopAfterLayout(CandidateList);

    private void OnFindMoreClick(object sender, RoutedEventArgs eventArgs)
    {
        var viewModel = (MainViewModel)DataContext;
        if (viewModel.SelectedCandidate is not { } currentCandidate)
        {
            return;
        }

        var candidates = viewModel.GetMergeCandidates();
        if (candidates.Count == 0)
        {
            MessageBox.Show(
                this,
                "There are no other suggestions in this year.",
                "Find More",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new MergeCandidateWindow(
            currentCandidate,
            candidates,
            viewModel.PicturesRoot,
            _previewWindowPlacementStore)
        {
            Owner = this,
        };
        if (window.ShowDialog() == true && window.CandidateToMerge is { } candidate)
        {
            viewModel.MergeCandidate(candidate);
        }
    }

    private void OnOpenSourceFolderClick(object sender, RoutedEventArgs eventArgs)
    {
        var sourceFolder = ((MainViewModel)DataContext).SourceFolderPath;
        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            return;
        }

        if (!Directory.Exists(sourceFolder))
        {
            MessageBox.Show(
                this,
                $"The source folder '{sourceFolder}' no longer exists.",
                "Open source folder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = sourceFolder,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is Win32Exception
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Open source folder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ScrollToTopAfterLayout(params ListBox[] lists)
    {
        _ = Dispatcher.InvokeAsync(
                () =>
                {
                    foreach (var list in lists)
                    {
                        if (list.Items.Count > 0)
                        {
                            list.ScrollIntoView(list.Items[0]);
                        }
                    }
                },
                DispatcherPriority.Loaded);
    }
}
