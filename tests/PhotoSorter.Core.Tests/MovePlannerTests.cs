using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class MovePlannerTests
{
    private static readonly DateTimeOffset CapturedAt = new(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Build_ValidInputs_ReturnsPlanWithExpectedDestination()
    {
        var sut = new MovePlanner();
        var bundle = CreateBundle(@"2023\Phone Images\IMG_0001.jpg", 1_024);
        var candidate = CreateCandidate(2023, [bundle]);

        var result = sut.Build(@"C:\Pictures", candidate, [bundle], "Birthday Party");

        Assert.IsTrue(result.IsValid);
        Assert.IsNotNull(result.Plan);
        Assert.AreEqual(@"2023\Birthday Party", result.Plan.DestinationDirectoryRelativePath);
        Assert.HasCount(1, result.Plan.Entries);
        Assert.AreEqual(
            @"2023\Birthday Party\IMG_0001.jpg",
            result.Plan.Entries[0].DestinationRelativePath);
        Assert.AreEqual(1_024, result.Plan.Entries[0].ExpectedLength);
    }

    [TestMethod]
    public void Build_InvalidFolderName_ReturnsValidatorErrorsAndNoPlan()
    {
        var sut = new MovePlanner();
        var bundle = CreateBundle(@"2023\Phone Images\IMG_0001.jpg", 1_024);
        var candidate = CreateCandidate(2023, [bundle]);

        var result = sut.Build(@"C:\Pictures", candidate, [bundle], "Phone Images");

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.Plan);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("'Phone Images' cannot be used", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Build_NoBundlesSelected_ReturnsError()
    {
        var sut = new MovePlanner();
        var candidate = CreateCandidate(2023, [CreateBundle(@"2023\Phone Images\IMG_0001.jpg", 1_024)]);

        var result = sut.Build(@"C:\Pictures", candidate, [], "Trip");

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("Select at least one item to move", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Build_BundleYearMismatch_ReturnsError()
    {
        var sut = new MovePlanner();
        var candidateBundle = CreateBundle(@"2023\Phone Images\IMG_0001.jpg", 1_024);
        var candidate = CreateCandidate(2023, [candidateBundle]);
        var wrongYearBundle = CreateBundle(@"2024\Phone Images\IMG_0002.jpg", 2_048, year: 2024);

        var result = sut.Build(@"C:\Pictures", candidate, [wrongYearBundle], "Trip");

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("must belong to the candidate year", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Build_SourceOutsidePhoneImages_ReturnsError()
    {
        var sut = new MovePlanner();
        var bundle = CreateBundle(@"2023\Screenshots\IMG_0001.jpg", 1_024);
        var candidate = CreateCandidate(2023, [bundle]);

        var result = sut.Build(@"C:\Pictures", candidate, [bundle], "Trip");

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("outside 2023\\Phone Images", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Build_DuplicateDestinationFileNames_ReturnsError()
    {
        var sut = new MovePlanner();
        var bundleA = CreateBundle(@"2023\Phone Images\Sub1\IMG_0001.jpg", 1_024, id: "a");
        var bundleB = CreateBundle(@"2023\Phone Images\Sub2\IMG_0001.jpg", 2_048, id: "b");
        var candidate = CreateCandidate(2023, [bundleA, bundleB]);

        var result = sut.Build(@"C:\Pictures", candidate, [bundleA, bundleB], "Trip");

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("More than one selected file would become", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Build_NullPicturesRoot_Throws()
    {
        var sut = new MovePlanner();
        var bundle = CreateBundle(@"2023\Phone Images\IMG_0001.jpg", 1_024);
        var candidate = CreateCandidate(2023, [bundle]);

        Assert.ThrowsExactly<ArgumentException>(() => sut.Build(string.Empty, candidate, [bundle], "Trip"));
    }

    private static CandidateGroup CreateCandidate(int year, IReadOnlyList<AssetBundle> bundles) => new()
    {
        Id = "candidate",
        Kind = CandidateKind.Event,
        Year = year,
        Start = CapturedAt,
        End = CapturedAt,
        Bundles = bundles,
        Areas = [],
        Reasons = [],
    };

    private static AssetBundle CreateBundle(string relativePath, long length, int? year = null, string id = "bundle") => new(
        id,
        [
            new MediaAsset
            {
                RelativePath = relativePath,
                Year = year ?? 2023,
                Extension = Path.GetExtension(relativePath),
                Kind = MediaKind.Image,
                CapturedAt = CapturedAt,
                TimestampConfidence = MetadataConfidence.High,
                Length = length,
                LastWriteTimeUtc = CapturedAt,
            },
        ]);
}
