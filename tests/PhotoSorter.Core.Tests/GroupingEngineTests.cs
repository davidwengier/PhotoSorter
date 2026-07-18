using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class GroupingEngineTests
{
    private static readonly DateTimeOffset Day1 = new(2023, 6, 1, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Analyze_FewCloseAnchorsSameDay_ProducesCompactEventWithComputedScore()
    {
        var sut = new GroupingEngine(new DecisionMatcher());
        var location = new GeoPoint(10.0, 10.0);
        AssetBundle[] bundles =
        [
            CreateBundle("e1", Day1, Offset(location, 0)),
            CreateBundle("e2", Day1.AddHours(1), Offset(location, 0.0002)),
            CreateBundle("e3", Day1.AddHours(2), Offset(location, -0.0002)),
        ];
        var options = new GroupingOptions
        {
            EventMinimumAnchors = 3,
            EventRadiusMeters = 1_000,
            EventMaximumGapHours = 4,
        };

        var result = sut.Analyze(bundles, new PhotoSorterState(), options);

        Assert.HasCount(1, result.Candidates);
        var candidate = result.Candidates[0];
        Assert.AreEqual(CandidateKind.Event, candidate.Kind);
        Assert.AreEqual(3, candidate.GpsBundleCount);
        CollectionAssert.AreEquivalent(
            new[] { "e1", "e2", "e3" },
            candidate.Bundles.Select(static bundle => bundle.Id).ToArray());

        var points = bundles.Select(static bundle => bundle.Location!.Value).ToArray();
        var center = GeoMath.Centroid(points);
        var radius = Math.Max(200, GeoMath.RadiusMeters(center, points) + 50);
        var expectedScore = Math.Min(100, 42 + (3 * 3) + Math.Max(0, 20 - (radius / 200)));
        Assert.AreEqual(expectedScore, candidate.Score, 1e-6);
    }

    [TestMethod]
    public void Analyze_ThreeDistantStopsWithinGap_ProducesMultiStopTrip()
    {
        var sut = new GroupingEngine(new DecisionMatcher());
        var stop1 = new GeoPoint(0.0, 0.0);
        var stop2 = new GeoPoint(0.0, 0.1);
        var stop3 = new GeoPoint(0.0, 0.2);
        AssetBundle[] bundles =
        [
            CreateBundle("s1a", Day1, stop1),
            CreateBundle("s1b", Day1.AddHours(1), stop1),
            CreateBundle("s2a", Day1.AddHours(20), stop2),
            CreateBundle("s2b", Day1.AddHours(21), stop2),
            CreateBundle("s3a", Day1.AddHours(40), stop3),
            CreateBundle("s3b", Day1.AddHours(41), stop3),
        ];
        var options = new GroupingOptions
        {
            EventMinimumAnchors = 2,
            EventRadiusMeters = 1_000,
            EventMaximumGapHours = 4,
            TripMaximumGapHours = 48,
            TripMinimumMovementMeters = 1_000,
            TripMinimumDurationHours = 1,
        };

        var result = sut.Analyze(bundles, new PhotoSorterState(), options);

        Assert.HasCount(1, result.Candidates);
        var trip = result.Candidates[0];
        Assert.AreEqual(CandidateKind.Trip, trip.Kind);
        Assert.HasCount(6, trip.Bundles);
        Assert.HasCount(3, trip.Areas);
        Assert.IsTrue(trip.Reasons[0].Contains("3 location segments", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Analyze_AnchorsNearSuppressedRoutineLocation_AreExcludedFromCandidates()
    {
        var sut = new GroupingEngine(new DecisionMatcher());
        var suppressedLocation = new GeoPoint(5.0, 5.0);
        var freeLocation = new GeoPoint(50.0, 50.0);
        AssetBundle[] bundles =
        [
            CreateBundle("home1", Day1, suppressedLocation),
            CreateBundle("home2", Day1.AddHours(1), suppressedLocation),
            CreateBundle("home3", Day1.AddHours(2), suppressedLocation),
            CreateBundle("trip1", Day1.AddDays(5), freeLocation),
            CreateBundle("trip2", Day1.AddDays(5).AddHours(1), freeLocation),
            CreateBundle("trip3", Day1.AddDays(5).AddHours(2), freeLocation),
        ];
        var state = new PhotoSorterState
        {
            RoutineLocations =
            [
                new RoutineLocationDecision
                {
                    Id = "home",
                    Name = "Home",
                    Center = suppressedLocation,
                    RadiusMeters = 500,
                },
            ],
        };
        var options = new GroupingOptions
        {
            EventMinimumAnchors = 3,
            EventRadiusMeters = 1_000,
            EventMaximumGapHours = 4,
        };

        var result = sut.Analyze(bundles, state, options);

        Assert.HasCount(1, result.Candidates);
        CollectionAssert.AreEquivalent(
            new[] { "trip1", "trip2", "trip3" },
            result.Candidates[0].Bundles.Select(static bundle => bundle.Id).ToArray());
    }

    [TestMethod]
    public void Analyze_NoGpsBundleWithinPadding_IsAttachedToNearestCandidate()
    {
        var sut = new GroupingEngine(new DecisionMatcher());
        var location = new GeoPoint(20.0, 20.0);
        var gpsBundles = new[]
        {
            CreateBundle("g1", Day1, location),
            CreateBundle("g2", Day1.AddHours(1), location),
            CreateBundle("g3", Day1.AddHours(2), location),
        };
        var noGpsBundle = CreateBundleWithoutGps("noGps", Day1.AddHours(3), MetadataConfidence.Medium);
        var options = new GroupingOptions
        {
            EventMinimumAnchors = 3,
            EventRadiusMeters = 1_000,
            EventMaximumGapHours = 4,
            AttachmentPaddingHours = 2,
        };

        var result = sut.Analyze([.. gpsBundles, noGpsBundle], new PhotoSorterState(), options);

        Assert.HasCount(1, result.Candidates);
        var candidate = result.Candidates[0];
        Assert.HasCount(4, candidate.Bundles);
        Assert.IsTrue(candidate.Bundles.Any(bundle => bundle.Id == "noGps"));
        Assert.IsTrue(
            candidate.Reasons[^1].Contains("1 time-adjacent items without GPS were attached", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Analyze_NoGpsBundleOutsidePadding_IsNotAttached()
    {
        var sut = new GroupingEngine(new DecisionMatcher());
        var location = new GeoPoint(20.0, 20.0);
        var gpsBundles = new[]
        {
            CreateBundle("g1", Day1, location),
            CreateBundle("g2", Day1.AddHours(1), location),
            CreateBundle("g3", Day1.AddHours(2), location),
        };
        var farBundle = CreateBundleWithoutGps("far", Day1.AddHours(10), MetadataConfidence.Medium);
        var options = new GroupingOptions
        {
            EventMinimumAnchors = 3,
            EventRadiusMeters = 1_000,
            EventMaximumGapHours = 4,
            AttachmentPaddingHours = 1,
        };

        var result = sut.Analyze([.. gpsBundles, farBundle], new PhotoSorterState(), options);

        Assert.HasCount(1, result.Candidates);
        Assert.HasCount(3, result.Candidates[0].Bundles);
        Assert.IsFalse(result.Candidates[0].Bundles.Any(bundle => bundle.Id == "far"));
    }

    [TestMethod]
    public void Analyze_IgnoredEventRule_ExcludesMatchingEventCandidate()
    {
        var decisionMatcher = new DecisionMatcher();
        var sut = new GroupingEngine(decisionMatcher);
        var location = new GeoPoint(30.0, 30.0);
        AssetBundle[] bundles =
        [
            CreateBundle("i1", Day1, location),
            CreateBundle("i2", Day1.AddHours(1), location),
            CreateBundle("i3", Day1.AddHours(2), location),
        ];
        var options = new GroupingOptions
        {
            EventMinimumAnchors = 3,
            EventRadiusMeters = 1_000,
            EventMaximumGapHours = 4,
        };

        var baseline = sut.Analyze(bundles, new PhotoSorterState(), options);
        Assert.HasCount(1, baseline.Candidates);
        var ignoredRule = decisionMatcher.CreateIgnoredRule(baseline.Candidates[0]);

        var stateWithIgnore = new PhotoSorterState { IgnoredGroups = [ignoredRule] };
        var result = sut.Analyze(bundles, stateWithIgnore, options);

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public void Analyze_IgnoredTripRule_ExcludesMatchingTripCandidate()
    {
        var decisionMatcher = new DecisionMatcher();
        var sut = new GroupingEngine(decisionMatcher);
        var stop1 = new GeoPoint(0.0, 0.0);
        var stop2 = new GeoPoint(0.0, 0.1);
        AssetBundle[] bundles =
        [
            CreateBundle("t1a", Day1, stop1),
            CreateBundle("t1b", Day1.AddHours(1), stop1),
            CreateBundle("t2a", Day1.AddHours(20), stop2),
            CreateBundle("t2b", Day1.AddHours(21), stop2),
        ];
        var options = new GroupingOptions
        {
            EventMinimumAnchors = 2,
            EventRadiusMeters = 1_000,
            EventMaximumGapHours = 4,
            TripMaximumGapHours = 48,
            TripMinimumMovementMeters = 1_000,
            TripMinimumDurationHours = 1,
        };

        var baseline = sut.Analyze(bundles, new PhotoSorterState(), options);
        Assert.HasCount(1, baseline.Candidates);
        Assert.AreEqual(CandidateKind.Trip, baseline.Candidates[0].Kind);
        var ignoredRule = decisionMatcher.CreateIgnoredRule(baseline.Candidates[0]);

        var stateWithIgnore = new PhotoSorterState { IgnoredGroups = [ignoredRule] };
        var result = sut.Analyze(bundles, stateWithIgnore, options);

        Assert.IsEmpty(result.Candidates);
    }

    [TestMethod]
    public void Analyze_AnchorsSpanningYearBoundary_ProduceSeparatePerYearCandidates()
    {
        var sut = new GroupingEngine(new DecisionMatcher());
        var location = new GeoPoint(15.0, 15.0);
        var newYearEve = new DateTimeOffset(2023, 12, 31, 23, 0, 0, TimeSpan.Zero);
        var newYearDay = new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero);
        AssetBundle[] bundles =
        [
            CreateBundle("y2023a", newYearEve, location, year: 2023),
            CreateBundle("y2023b", newYearEve.AddMinutes(30), location, year: 2023),
            CreateBundle("y2024a", newYearDay, location, year: 2024),
            CreateBundle("y2024b", newYearDay.AddMinutes(30), location, year: 2024),
        ];
        var options = new GroupingOptions
        {
            EventMinimumAnchors = 2,
            EventRadiusMeters = 1_000,
            EventMaximumGapHours = 4,
        };

        var result = sut.Analyze(bundles, new PhotoSorterState(), options);

        Assert.HasCount(2, result.Candidates);
        CollectionAssert.AreEquivalent(new[] { 2023, 2024 }, result.Candidates.Select(static c => c.Year).ToArray());
        var candidate2023 = result.Candidates.Single(static c => c.Year == 2023);
        var candidate2024 = result.Candidates.Single(static c => c.Year == 2024);
        CollectionAssert.AreEquivalent(
            new[] { "y2023a", "y2023b" },
            candidate2023.Bundles.Select(static bundle => bundle.Id).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "y2024a", "y2024b" },
            candidate2024.Bundles.Select(static bundle => bundle.Id).ToArray());
    }

    private static GeoPoint Offset(GeoPoint point, double delta) => new(point.Latitude + delta, point.Longitude + delta);

    private static AssetBundle CreateBundle(string id, DateTimeOffset capturedAt, GeoPoint location, int? year = null) => new(
        id,
        [
            new MediaAsset
            {
                RelativePath = $@"{year ?? capturedAt.Year}\Phone Images\{id}.jpg",
                Year = year ?? capturedAt.Year,
                Extension = ".jpg",
                Kind = MediaKind.Image,
                CapturedAt = capturedAt,
                TimestampConfidence = MetadataConfidence.High,
                Location = location,
            },
        ]);

    private static AssetBundle CreateBundleWithoutGps(string id, DateTimeOffset capturedAt, MetadataConfidence confidence) => new(
        id,
        [
            new MediaAsset
            {
                RelativePath = $@"{capturedAt.Year}\Phone Images\{id}.jpg",
                Year = capturedAt.Year,
                Extension = ".jpg",
                Kind = MediaKind.Image,
                CapturedAt = capturedAt,
                TimestampConfidence = confidence,
                Location = null,
            },
        ]);
}
