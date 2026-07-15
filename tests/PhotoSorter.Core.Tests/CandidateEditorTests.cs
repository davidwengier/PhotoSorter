using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class CandidateEditorTests
{
    private static readonly DateTimeOffset BaseTime = new(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void SelectBundles_SubsetOfIds_ReturnsCandidateWithOnlySelectedBundles()
    {
        var sut = new CandidateEditor();
        var bundleA = CreateBundle("a", BaseTime);
        var bundleB = CreateBundle("b", BaseTime.AddHours(1));
        var candidate = CreateCandidate([bundleA, bundleB]);

        var result = sut.SelectBundles(candidate, ["a"]);

        Assert.HasCount(1, result.Bundles);
        Assert.AreEqual("a", result.Bundles[0].Id);
        Assert.AreEqual(bundleA.CapturedAt, result.Start);
        Assert.AreEqual(bundleA.CapturedAt, result.End);
    }

    [TestMethod]
    public void SelectBundles_NoMatchingIds_Throws()
    {
        var sut = new CandidateEditor();
        var candidate = CreateCandidate([CreateBundle("a", BaseTime)]);

        Assert.ThrowsExactly<ArgumentException>(() => sut.SelectBundles(candidate, ["missing"]));
    }

    [TestMethod]
    public void SelectBundles_NullCandidate_Throws()
    {
        var sut = new CandidateEditor();

        Assert.ThrowsExactly<ArgumentNullException>(() => sut.SelectBundles(null!, ["a"]));
    }

    [TestMethod]
    public void Split_BoundaryBetweenBundles_ProducesBeforeAndAfterGroups()
    {
        var sut = new CandidateEditor();
        var earlyBundle = CreateBundle("early", BaseTime);
        var lateBundle = CreateBundle("late", BaseTime.AddHours(3));
        var candidate = CreateCandidate([earlyBundle, lateBundle]);

        var (before, after) = sut.Split(candidate, BaseTime.AddHours(1));

        Assert.HasCount(1, before.Bundles);
        Assert.AreEqual("early", before.Bundles[0].Id);
        Assert.HasCount(1, after.Bundles);
        Assert.AreEqual("late", after.Bundles[0].Id);
    }

    [TestMethod]
    public void Split_BoundaryLeavesOneSideEmpty_Throws()
    {
        var sut = new CandidateEditor();
        var candidate = CreateCandidate([CreateBundle("a", BaseTime), CreateBundle("b", BaseTime.AddHours(1))]);

        Assert.ThrowsExactly<ArgumentException>(() => sut.Split(candidate, BaseTime.AddDays(-1)));
    }

    [TestMethod]
    public void Merge_TwoEventsSameYear_CombinesBundlesAndKeepsEventKind()
    {
        var sut = new CandidateEditor();
        var first = CreateCandidate([CreateBundle("a", BaseTime)], kind: CandidateKind.Event);
        var second = CreateCandidate([CreateBundle("b", BaseTime.AddHours(1))], kind: CandidateKind.Event);

        var merged = sut.Merge(first, second);

        Assert.HasCount(2, merged.Bundles);
        Assert.AreEqual(CandidateKind.Event, merged.Kind);
        Assert.IsTrue(merged.Reasons.Contains("Merged manually."));
    }

    [TestMethod]
    public void Merge_EitherCandidateIsTrip_ResultIsTrip()
    {
        var sut = new CandidateEditor();
        var first = CreateCandidate([CreateBundle("a", BaseTime)], kind: CandidateKind.Event);
        var second = CreateCandidate([CreateBundle("b", BaseTime.AddHours(1))], kind: CandidateKind.Trip);

        var merged = sut.Merge(first, second);

        Assert.AreEqual(CandidateKind.Trip, merged.Kind);
    }

    [TestMethod]
    public void Merge_DifferentYears_Throws()
    {
        var sut = new CandidateEditor();
        var first = CreateCandidate([CreateBundle("a", BaseTime)]);
        var second = CreateCandidate([CreateBundle("b", BaseTime.AddYears(1))]);

        Assert.ThrowsExactly<ArgumentException>(() => sut.Merge(first, second));
    }

    [TestMethod]
    public void Merge_DuplicateBundleAcrossBoth_DeduplicatesById()
    {
        var sut = new CandidateEditor();
        var shared = CreateBundle("shared", BaseTime);
        var first = CreateCandidate([shared]);
        var second = CreateCandidate([shared, CreateBundle("unique", BaseTime.AddHours(1))]);

        var merged = sut.Merge(first, second);

        Assert.HasCount(2, merged.Bundles);
    }

    [TestMethod]
    public void AddBundles_MixedYears_KeepsOnlyMatchingYear()
    {
        var sut = new CandidateEditor();
        var candidate = CreateCandidate([CreateBundle("a", BaseTime)]);
        var sameYear = CreateBundle("b", BaseTime.AddDays(1));
        var otherYear = CreateBundle("c", BaseTime.AddYears(1));

        var result = sut.AddBundles(candidate, [sameYear, otherYear]);

        Assert.HasCount(2, result.Bundles);
        Assert.IsFalse(result.Bundles.Any(bundle => bundle.Id == "c"));
    }

    [TestMethod]
    public void AddBundles_NullAdditionalBundles_Throws()
    {
        var sut = new CandidateEditor();
        var candidate = CreateCandidate([CreateBundle("a", BaseTime)]);

        Assert.ThrowsExactly<ArgumentNullException>(() => sut.AddBundles(candidate, null!));
    }

    private static CandidateGroup CreateCandidate(
        IReadOnlyList<AssetBundle> bundles,
        CandidateKind kind = CandidateKind.Event) => new()
        {
            Id = "candidate",
            Kind = kind,
            Year = bundles[0].Year,
            Start = bundles.Min(static bundle => bundle.CapturedAt),
            End = bundles.Max(static bundle => bundle.CapturedAt),
            Bundles = bundles,
            Areas = [],
            Reasons = [],
        };

    private static AssetBundle CreateBundle(string id, DateTimeOffset capturedAt) => new(
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
}
