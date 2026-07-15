namespace PhotoSorter.Core.Models;

public readonly record struct GeoPoint(double Latitude, double Longitude);

public sealed record GeoCircle
{
    public required GeoPoint Center { get; init; }

    public double RadiusMeters { get; init; }
}

public sealed record PlaceName(
    string ShortName,
    string DisplayName,
    bool IsPointOfInterest = false);
