using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoSorter.App.ViewModels;
using PhotoSorter.Core.Models;
using PhotoSorter.Infrastructure.Cache;

namespace PhotoSorter.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MergeCandidateWindowTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2024, 4, 10, 10, 0, 0, TimeSpan.Zero);

    [STATestMethod]
    public void Constructor_ReadOnlyHeaderBindings_DoesNotThrow()
    {
        EnsureApplicationResources();
        var current = new CandidateViewModel(CreateCandidate(
            "current",
            "Current place",
            BaseTime));
        var option = new CandidateViewModel(CreateCandidate(
            "option",
            "Possible place",
            BaseTime.AddDays(1)));
        var placementStore = new PreviewWindowPlacementStore(
            Path.GetTempPath(),
            NullLogger<PreviewWindowPlacementStore>.Instance);

        var window = new MergeCandidateWindow(
            current,
            [option],
            Path.GetTempPath(),
            placementStore);
        window.Measure(new Size(1180, 780));
        window.Arrange(new Rect(0, 0, 1180, 780));
        window.UpdateLayout();

        Assert.AreSame(current, window.ViewModel.CurrentCandidate);

        window.Close();
    }

    private static void EnsureApplicationResources()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var application = new App();
        application.InitializeComponent();
    }

    private static CandidateGroup CreateCandidate(
        string id,
        string place,
        DateTimeOffset capturedAt)
    {
        var bundle = new AssetBundle(
            id,
            [
                new MediaAsset
                {
                    RelativePath = $@"{capturedAt.Year}\Phone Images\{id}.jpg",
                    Year = capturedAt.Year,
                    Extension = ".jpg",
                    Kind = MediaKind.Image,
                    CapturedAt = capturedAt,
                    TimestampConfidence = MetadataConfidence.High,
                },
            ]);
        return new CandidateGroup
        {
            Id = id,
            Kind = CandidateKind.Event,
            Year = capturedAt.Year,
            Start = capturedAt,
            End = capturedAt,
            Bundles = [bundle],
            Areas = [],
            Score = 80,
            Reasons = ["Test candidate."],
            PlaceLabel = place,
            FullPlaceLabel = place,
            PrimaryPlaceLabel = place,
        };
    }
}
