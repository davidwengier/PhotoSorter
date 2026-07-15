using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoSorter.Core.Models;

namespace PhotoSorter.App.ViewModels;

public sealed partial class CandidateViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _placeLabel;

    [ObservableProperty]
    private bool _isNaming;

    [ObservableProperty]
    private string? _fullPlaceName;

    public CandidateViewModel(CandidateGroup model)
    {
        Model = model;
        _placeLabel = model.PlaceLabel;
        _fullPlaceName = model.FullPlaceLabel;
    }

    public CandidateGroup Model { get; private set; }

    public string Title => !string.IsNullOrWhiteSpace(PlaceLabel)
        ? PlaceLabel
        : IsNaming
            ? "Finding location..."
            : $"Photos from {Model.Start:d MMM yyyy}";

    public string Period => Model.Start.Date == Model.End.Date
        ? $"{Model.Start:ddd d MMM yyyy, HH:mm}–{Model.End:HH:mm}"
        : $"{Model.Start:ddd d MMM HH:mm} – {Model.End:ddd d MMM HH:mm}";

    public string Summary => $"{Model.Bundles.Count:N0} items · {Model.FileCount:N0} files";

    public double Confidence => Model.Score;

    public string ConfidenceText => $"{Model.Score:0}% confidence";

    public string WhySuggested => Model.Reasons.Count > 0
        ? Model.Reasons[0]
        : "Grouped by capture time and location.";

    public string? PrimaryPlaceLabel => Model.PrimaryPlaceLabel ?? PlaceLabel;

    public void ApplyPlace(string label, string fullLabel, string primaryPlaceLabel)
    {
        Model = Model with
        {
            PlaceLabel = label,
            FullPlaceLabel = fullLabel,
            PrimaryPlaceLabel = primaryPlaceLabel,
        };
        PlaceLabel = label;
        FullPlaceName = fullLabel;
        OnPropertyChanged(nameof(PrimaryPlaceLabel));
    }

    partial void OnPlaceLabelChanged(string? value) => OnPropertyChanged(nameof(Title));

    partial void OnIsNamingChanged(bool value) => OnPropertyChanged(nameof(Title));
}

public sealed partial class BundleViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isIncluded = true;

    public BundleViewModel(string picturesRoot, AssetBundle model)
    {
        Model = model;
        ThumbnailPath = Path.Combine(picturesRoot, model.PrimaryAsset.RelativePath);
        SourceFolder = Path.Combine(picturesRoot, model.PrimaryAsset.DirectoryRelativePath);
    }

    public AssetBundle Model { get; }

    public string ThumbnailPath { get; }

    public long ThumbnailLength => Model.PrimaryAsset.Length;

    public DateTimeOffset ThumbnailLastWriteTimeUtc => Model.PrimaryAsset.LastWriteTimeUtc;

    public string FileName => Model.PrimaryAsset.FileName;

    public string Files => string.Join(", ", Model.Assets.Select(static asset => asset.FileName));

    public string SourceFolder { get; }

    public string Captured => Model.CapturedAt.ToString("ddd d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    public string Location => Model.Location is { } point
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{point.Latitude:0.00000}, {point.Longitude:0.00000}")
        : "No GPS";

    public string Metadata
    {
        get
        {
            var timestampSource = Model.PrimaryAsset.TimestampSource switch
            {
                TimestampSource.ExifOriginal => "Photo metadata",
                TimestampSource.MediaCreated => "Media metadata",
                TimestampSource.FileName => "File name timestamp",
                TimestampSource.FileSystem => "File modified timestamp",
                _ => "Timestamp",
            };
            var dimensions = Model.PrimaryAsset is { Width: { } width, Height: { } height }
                ? $"{width:N0} x {height:N0} · "
                : string.Empty;
            var linkedFiles = Model.Assets.Count > 1
                ? $" · {Model.Assets.Count:N0} linked files"
                : string.Empty;
            return $"{dimensions}{timestampSource} · {Model.TimestampConfidence} confidence{linkedFiles}";
        }
    }
}
