using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using PhotoSorter.Core.Contracts;

namespace PhotoSorter.App.Controls;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The WPF control disposes its cancellation source whenever it is unloaded.")]
public sealed class LazyThumbnail : Image
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath),
        typeof(string),
        typeof(LazyThumbnail),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty FileLengthProperty = DependencyProperty.Register(
        nameof(FileLength),
        typeof(long),
        typeof(LazyThumbnail),
        new PropertyMetadata(0L, OnInputChanged));

    public static readonly DependencyProperty LastWriteTimeUtcProperty = DependencyProperty.Register(
        nameof(LastWriteTimeUtc),
        typeof(DateTimeOffset),
        typeof(LazyThumbnail),
        new PropertyMetadata(default(DateTimeOffset), OnInputChanged));

    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
        nameof(PixelSize),
        typeof(int),
        typeof(LazyThumbnail),
        new PropertyMetadata(128, OnInputChanged));

    private CancellationTokenSource? _loadCancellation;

    public LazyThumbnail()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Stretch = System.Windows.Media.Stretch.Uniform;
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public long FileLength
    {
        get => (long)GetValue(FileLengthProperty);
        set => SetValue(FileLengthProperty, value);
    }

    public DateTimeOffset LastWriteTimeUtc
    {
        get => (DateTimeOffset)GetValue(LastWriteTimeUtcProperty);
        set => SetValue(LastWriteTimeUtcProperty, value);
    }

    public int PixelSize
    {
        get => (int)GetValue(PixelSizeProperty);
        set => SetValue(PixelSizeProperty, value);
    }

    private static void OnInputChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is LazyThumbnail thumbnail && thumbnail.IsLoaded)
        {
            thumbnail.StartLoad();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs) => StartLoad();

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private async void StartLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        Source = null;

        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            ToolTip = "Thumbnail unavailable";
            return;
        }

        try
        {
            var service = ((App)Application.Current).Services.GetRequiredService<IThumbnailService>();
            var bytes = await service.GetThumbnailAsync(
                FilePath,
                FileLength,
                LastWriteTimeUtc,
                PixelSize,
                cancellationToken);
            if (bytes is null || cancellationToken.IsCancellationRequested)
            {
                ToolTip = "No Windows thumbnail codec is available for this file.";
                return;
            }

            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            Source = bitmap;
            ToolTip = FilePath;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ToolTip = exception.Message;
        }
    }
}
