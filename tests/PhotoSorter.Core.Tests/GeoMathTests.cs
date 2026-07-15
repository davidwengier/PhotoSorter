using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class GeoMathTests
{
    [TestMethod]
    public void DistanceMeters_SamePoint_ReturnsZero()
    {
        var point = new GeoPoint(51.5074, -0.1278);

        var distance = GeoMath.DistanceMeters(point, point);

        Assert.AreEqual(0, distance, 1e-6);
    }

    [TestMethod]
    public void DistanceMeters_KnownCities_MatchesExpectedApproximateDistance()
    {
        // London to Paris is approximately 343-344 km great-circle distance.
        var london = new GeoPoint(51.5074, -0.1278);
        var paris = new GeoPoint(48.8566, 2.3522);

        var distance = GeoMath.DistanceMeters(london, paris);

        Assert.AreEqual(343_500, distance, 5_000);
    }

    [TestMethod]
    public void DistanceMeters_IsSymmetric()
    {
        var first = new GeoPoint(10, 20);
        var second = new GeoPoint(-5, 30);

        var forward = GeoMath.DistanceMeters(first, second);
        var backward = GeoMath.DistanceMeters(second, first);

        Assert.AreEqual(forward, backward, 1e-9);
    }

    [TestMethod]
    public void Centroid_MultiplePoints_ReturnsAverage()
    {
        GeoPoint[] points = [new(0, 0), new(10, 20), new(20, 40)];

        var centroid = GeoMath.Centroid(points);

        Assert.AreEqual(10, centroid.Latitude, 1e-9);
        Assert.AreEqual(20, centroid.Longitude, 1e-9);
    }

    [TestMethod]
    public void Centroid_EmptyCollection_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => GeoMath.Centroid([]));
    }

    [TestMethod]
    public void Centroid_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => GeoMath.Centroid(null!));
    }

    [TestMethod]
    public void RadiusMeters_ReturnsMaximumDistanceFromCenter()
    {
        var center = new GeoPoint(0, 0);
        GeoPoint[] points = [new(0, 0.001), new(0, 0.01)];

        var radius = GeoMath.RadiusMeters(center, points);
        var expectedMax = GeoMath.DistanceMeters(center, new GeoPoint(0, 0.01));

        Assert.AreEqual(expectedMax, radius, 1e-6);
    }

    [TestMethod]
    public void RadiusMeters_EmptyCollection_ReturnsZero()
    {
        var radius = GeoMath.RadiusMeters(new GeoPoint(0, 0), []);

        Assert.AreEqual(0, radius);
    }

    [TestMethod]
    public void Contains_PointInsideRadius_ReturnsTrue()
    {
        var circle = new GeoCircle { Center = new GeoPoint(0, 0), RadiusMeters = 2_000 };

        var result = GeoMath.Contains(circle, new GeoPoint(0, 0.001));

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Contains_PointOutsideRadius_ReturnsFalse()
    {
        var circle = new GeoCircle { Center = new GeoPoint(0, 0), RadiusMeters = 10 };

        var result = GeoMath.Contains(circle, new GeoPoint(0, 1));

        Assert.IsFalse(result);
    }
}
