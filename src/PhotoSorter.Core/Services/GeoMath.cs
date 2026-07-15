using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Services;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_008.8;

    public static double DistanceMeters(GeoPoint first, GeoPoint second)
    {
        var latitude1 = DegreesToRadians(first.Latitude);
        var latitude2 = DegreesToRadians(second.Latitude);
        var latitudeDelta = latitude2 - latitude1;
        var longitudeDelta = DegreesToRadians(second.Longitude - first.Longitude);

        var haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + (Math.Cos(latitude1) * Math.Cos(latitude2) * Math.Pow(Math.Sin(longitudeDelta / 2), 2));

        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(haversine)));
    }

    public static GeoPoint Centroid(IEnumerable<GeoPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var materialized = points.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("At least one point is required.", nameof(points));
        }

        var latitude = materialized.Average(static point => point.Latitude);
        var longitude = materialized.Average(static point => point.Longitude);
        return new GeoPoint(latitude, longitude);
    }

    public static double RadiusMeters(GeoPoint center, IEnumerable<GeoPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return points.Select(point => DistanceMeters(center, point)).DefaultIfEmpty().Max();
    }

    public static bool Contains(GeoCircle circle, GeoPoint point) =>
        DistanceMeters(circle.Center, point) <= circle.RadiusMeters;

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
