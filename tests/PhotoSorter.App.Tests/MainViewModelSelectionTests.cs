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

    [TestMethod]
    public void ResolveCandidateSelection_CurrentRemoved_SelectsNextSurvivingCandidate()
    {
        var first = new CandidateViewModel(CreateCandidate("first"));
        var later = new CandidateViewModel(CreateCandidate("later"));

        var selected = MainViewModel.ResolveCandidateSelection(
            [first, later],
            "current",
            ["next", "later", "first"]);

        Assert.AreSame(later, selected);
    }

    [TestMethod]
    public void ResolveCandidateSelection_NoLaterCandidate_SelectsPreviousCandidate()
    {
        var previous = new CandidateViewModel(CreateCandidate("previous"));

        var selected = MainViewModel.ResolveCandidateSelection(
            [previous],
            "current",
            ["next", "previous"]);

        Assert.AreSame(previous, selected);
    }

    private static CandidateGroup CreateCandidate(string id = "candidate")
    {
        var capturedAt = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var bundles = Enumerable.Range(1, 3)
            .Select(index => new AssetBundle(
                $"{id}-bundle-{index}",
                [
                    new MediaAsset
                    {
                        RelativePath = $@"2023\Phone Images\{id}-photo-{index}.jpg",
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
            Id = id,
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
