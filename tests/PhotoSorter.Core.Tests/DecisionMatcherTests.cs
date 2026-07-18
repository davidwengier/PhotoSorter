using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class DecisionMatcherTests
{
    [TestMethod]
    public void IsSuppressedRoutine_MatchingRoutine_ReturnsTrue()
    {
        var sut = new DecisionMatcher();
        var decision = new RoutineLocationDecision
        {
            Id = "d1",
            Name = "Home",
            Center = new GeoPoint(51.0, 0.0),
            RadiusMeters = 200,
        };

        var result = sut.IsSuppressedRoutine(new GeoPoint(51.0, 0.0), [decision]);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsIgnored_MatchingKindTimeAndFraction_ReturnsTrue()
    {
        var sut = new DecisionMatcher();
        var start = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var rule = new IgnoredGroupRule
        {
            Id = "g1",
            Kind = CandidateKind.Event,
            Start = start,
            End = start.AddHours(2),
            TimePaddingMinutes = 30,
            RequiredLocationMatchFraction = 0.5,
            Areas = [new GeoCircle { Center = new GeoPoint(10, 10), RadiusMeters = 500 }],
        };
        var candidate = CreateCandidate(CandidateKind.Event, start.AddMinutes(30), start.AddHours(1), new GeoPoint(10, 10));

        var result = sut.IsIgnored(candidate, [rule]);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsIgnored_DifferentKind_ReturnsFalse()
    {
        var sut = new DecisionMatcher();
        var start = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var rule = new IgnoredGroupRule
        {
            Id = "g1",
            Kind = CandidateKind.Trip,
            Start = start,
            End = start.AddHours(2),
            Areas = [new GeoCircle { Center = new GeoPoint(10, 10), RadiusMeters = 500 }],
        };
        var candidate = CreateCandidate(CandidateKind.Event, start.AddMinutes(30), start.AddHours(1), new GeoPoint(10, 10));

        var result = sut.IsIgnored(candidate, [rule]);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsIgnored_OutsideTimeWindow_ReturnsFalse()
    {
        var sut = new DecisionMatcher();
        var start = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var rule = new IgnoredGroupRule
        {
            Id = "g1",
            Kind = CandidateKind.Event,
            Start = start,
            End = start.AddHours(1),
            TimePaddingMinutes = 5,
            Areas = [new GeoCircle { Center = new GeoPoint(10, 10), RadiusMeters = 500 }],
        };
        var candidate = CreateCandidate(CandidateKind.Event, start.AddHours(5), start.AddHours(6), new GeoPoint(10, 10));

        var result = sut.IsIgnored(candidate, [rule]);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsIgnored_BelowRequiredFraction_ReturnsFalse()
    {
        var sut = new DecisionMatcher();
        var start = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var rule = new IgnoredGroupRule
        {
            Id = "g1",
            Kind = CandidateKind.Event,
            Start = start,
            End = start.AddHours(2),
            RequiredLocationMatchFraction = 0.9,
            Areas = [new GeoCircle { Center = new GeoPoint(10, 10), RadiusMeters = 500 }],
        };
        var candidate = CreateCandidate(
            CandidateKind.Event,
            start.AddMinutes(30),
            start.AddHours(1),
            new GeoPoint(10, 10),
            new GeoPoint(50, 50));

        var result = sut.IsIgnored(candidate, [rule]);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsIgnored_NoGpsBundles_ReturnsFalse()
    {
        var sut = new DecisionMatcher();
        var start = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var rule = new IgnoredGroupRule
        {
            Id = "g1",
            Kind = CandidateKind.Event,
            Start = start,
            End = start.AddHours(2),
            Areas = [new GeoCircle { Center = new GeoPoint(10, 10), RadiusMeters = 500 }],
        };
        var bundle = new AssetBundle("b1", [CreateAsset(@"2023\Phone Images\a.jpg", start.AddMinutes(30), location: null)]);
        var candidate = new CandidateGroup
        {
            Id = "c1",
            Kind = CandidateKind.Event,
            Year = 2023,
            Start = start.AddMinutes(30),
            End = start.AddHours(1),
            Bundles = [bundle],
            Areas = [],
            Reasons = [],
        };

        var result = sut.IsIgnored(candidate, [rule]);

        Assert.IsFalse(result);
    }

    [TestMethod]
    [DataRow(CandidateKind.Trip, 360, 0.3)]
    [DataRow(CandidateKind.Event, 90, 0.5)]
    public void CreateIgnoredRule_UsesKindSpecificDefaults(CandidateKind kind, int expectedPadding, double expectedFraction)
    {
        var sut = new DecisionMatcher();
        var start = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var candidate = CreateCandidate(kind, start, start.AddHours(1), new GeoPoint(1, 1));

        var rule = sut.CreateIgnoredRule(candidate, "  My Label  ");

        Assert.AreEqual(kind, rule.Kind);
        Assert.AreEqual(expectedPadding, rule.TimePaddingMinutes);
        Assert.AreEqual(expectedFraction, rule.RequiredLocationMatchFraction);
        Assert.AreEqual("My Label", rule.Label);
    }

    [TestMethod]
    public void CreateIgnoredRule_NullOrWhitespaceLabel_ResultsInNullLabel()
    {
        var sut = new DecisionMatcher();
        var start = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var candidate = CreateCandidate(CandidateKind.Event, start, start.AddHours(1), new GeoPoint(1, 1));

        var rule = sut.CreateIgnoredRule(candidate, "   ");

        Assert.IsNull(rule.Label);
    }

    private static CandidateGroup CreateCandidate(
        CandidateKind kind,
        DateTimeOffset start,
        DateTimeOffset end,
        params GeoPoint[] bundleLocations)
    {
        var bundles = bundleLocations
            .Select((location, index) => new AssetBundle(
                $"b{index}",
                [CreateAsset($@"2023\Phone Images\a{index}.jpg", start, location)]))
            .ToArray();
        return new CandidateGroup
        {
            Id = "c1",
            Kind = kind,
            Year = start.Year,
            Start = start,
            End = end,
            Bundles = bundles,
            Areas = [],
            Reasons = [],
        };
    }

    private static MediaAsset CreateAsset(string relativePath, DateTimeOffset capturedAt, GeoPoint? location) => new()
    {
        RelativePath = relativePath,
        Year = capturedAt.Year,
        Extension = Path.GetExtension(relativePath),
        Kind = MediaKind.Image,
        CapturedAt = capturedAt,
        TimestampConfidence = MetadataConfidence.High,
        Location = location,
    };
}
