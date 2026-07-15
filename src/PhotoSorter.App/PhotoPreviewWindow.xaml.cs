using System.ComponentModel;
using System.Windows.Input;
using PhotoSorter.App.ViewModels;
using PhotoSorter.Infrastructure.Cache;

namespace PhotoSorter.App;

public partial class PhotoPreviewWindow : Window
{
    private const double MinimumVisibleEdge = 80;

    private readonly IReadOnlyList<BundleViewModel> _bundles;
    private readonly PreviewWindowPlacementStore _placementStore;
    private int _currentIndex;
    private WindowState _lastNonMinimizedState = WindowState.Normal;

    public PhotoPreviewWindow(
        IReadOnlyList<BundleViewModel> bundles,
        BundleViewModel initialBundle,
        PreviewWindowPlacementStore placementStore)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentNullException.ThrowIfNull(initialBundle);
        ArgumentNullException.ThrowIfNull(placementStore);
        if (bundles.Count == 0)
        {
            throw new ArgumentException("At least one photo is required.", nameof(bundles));
        }

        _bundles = bundles;
        _placementStore = placementStore;
        _currentIndex = FindInitialIndex(initialBundle);

        InitializeComponent();
        DataContext = _bundles[_currentIndex];
        ApplyPlacement(_placementStore.Load());
        UpdateNavigation();
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    private void OnPreviousClick(object sender, RoutedEventArgs eventArgs) => Navigate(-1);

    private void OnNextClick(object sender, RoutedEventArgs eventArgs) => Navigate(1);

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Left && PreviousButton.IsEnabled)
        {
            Navigate(-1);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Right && NextButton.IsEnabled)
        {
            Navigate(1);
            eventArgs.Handled = true;
        }
    }

    private void Navigate(int offset)
    {
        var newIndex = _currentIndex + offset;
        if (newIndex < 0 || newIndex >= _bundles.Count)
        {
            return;
        }

        _currentIndex = newIndex;
        DataContext = _bundles[_currentIndex];
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        PreviousButton.IsEnabled = _currentIndex > 0;
        NextButton.IsEnabled = _currentIndex < _bundles.Count - 1;
        PositionText.Text = $"{_currentIndex + 1:N0} of {_bundles.Count:N0}";
    }

    private int FindInitialIndex(BundleViewModel initialBundle)
    {
        for (var index = 0; index < _bundles.Count; index++)
        {
            if (ReferenceEquals(_bundles[index], initialBundle))
            {
                return index;
            }
        }

        return 0;
    }

    private void ApplyPlacement(PreviewWindowPlacement? placement)
    {
        if (placement is null)
        {
            return;
        }

        var virtualBounds = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var width = Math.Clamp(
            placement.Width,
            MinWidth,
            Math.Max(MinWidth, virtualBounds.Width));
        var height = Math.Clamp(
            placement.Height,
            MinHeight,
            Math.Max(MinHeight, virtualBounds.Height));
        var savedBounds = new Rect(placement.Left, placement.Top, width, height);
        var visibleBounds = Rect.Intersect(savedBounds, virtualBounds);
        if (visibleBounds.IsEmpty
            || visibleBounds.Width < MinimumVisibleEdge
            || visibleBounds.Height < MinimumVisibleEdge)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width;
        Height = height;
        Left = Math.Clamp(
            placement.Left,
            virtualBounds.Left - width + MinimumVisibleEdge,
            virtualBounds.Right - MinimumVisibleEdge);
        Top = Math.Clamp(
            placement.Top,
            virtualBounds.Top - height + MinimumVisibleEdge,
            virtualBounds.Bottom - MinimumVisibleEdge);

        if (placement.IsMaximized)
        {
            Loaded += OnLoadedMaximized;
        }
    }

    private void OnLoadedMaximized(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoadedMaximized;
        WindowState = WindowState.Maximized;
    }

    private void OnStateChanged(object? sender, EventArgs eventArgs)
    {
        if (WindowState != WindowState.Minimized)
        {
            _lastNonMinimizedState = WindowState;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        _placementStore.Save(new PreviewWindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = _lastNonMinimizedState == WindowState.Maximized,
        });
    }
}
