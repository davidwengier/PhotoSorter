using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoSorter.App.ViewModels;
using PhotoSorter.Core.Models;

namespace PhotoSorter.App.Tests;

[TestClass]
public sealed class MainViewModelSelectionTests
{
    [TestMethod]
    public void IncludedItemsSummary_IncludeChanges_ReportsSelectedOfTotal()
    {
        using var viewModel = new MainViewModel(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<MainViewModel>.Instance)
        {
            PicturesRoot = Path.GetTempPath(),
            SelectedCandidate = new CandidateViewModel(CreateCandidate()),
        };

        Assert.AreEqual("3 of 3 items included", viewModel.IncludedItemsSummary);

        viewModel.SelectedBundles[0].IsIncluded = false;

        Assert.AreEqual("2 of 3 items included", viewModel.IncludedItemsSummary);
    }

    private static CandidateGroup CreateCandidate()
    {
        var capturedAt = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var bundles = Enumerable.Range(1, 3)
            .Select(index => new AssetBundle(
                $"bundle-{index}",
                [
                    new MediaAsset
                    {
                        RelativePath = $@"2023\Phone Images\photo-{index}.jpg",
                        Year = 2023,
                        Extension = ".jpg",
                        Kind = MediaKind.Image,
                        CapturedAt = capturedAt.AddMinutes(index),
                        TimestampConfidence = MetadataConfidence.High,
                    },
                ]))
            .ToArray();
        return new CandidateGroup
        {
            Id = "candidate",
            Kind = CandidateKind.Event,
            Year = 2023,
            Start = bundles[0].CapturedAt,
            End = bundles[^1].CapturedAt,
            Bundles = bundles,
            Areas = [],
            Reasons = [],
        };
    }
}
