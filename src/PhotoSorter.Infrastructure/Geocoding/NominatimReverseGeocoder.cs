using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Infrastructure.Geocoding;

public sealed class NominatimReverseGeocoder(
    HttpClient httpClient,
    IMediaCacheFactory cacheFactory) : IReverseGeocoder, IDisposable
{
    private static readonly Uri Endpoint = new("https://nominatim.openstreetmap.org/");
    private static readonly HashSet<string> PointOfInterestCategories = new(
        [
            "historic",
            "leisure",
            "man_made",
            "natural",
            "sport",
            "tourism",
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AmenityPointOfInterestTypes = new(
        [
            "arts_centre",
            "cinema",
            "college",
            "community_centre",
            "conference_centre",
            "events_venue",
            "exhibition_centre",
            "kindergarten",
            "library",
            "place_of_worship",
            "school",
            "theatre",
            "university",
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BuildingPointOfInterestTypes = new(
        [
            "cathedral",
            "chapel",
            "church",
            "college",
            "hotel",
            "museum",
            "school",
            "stadium",
            "train_station",
            "university",
        ],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AerowayPointOfInterestTypes = new(
        ["aerodrome", "terminal"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RailwayPointOfInterestTypes = new(
        ["halt", "station", "tram_stop"],
        StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly HttpClient _httpClient = httpClient;
    private readonly IMediaCacheFactory _cacheFactory = cacheFactory;
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public string Attribution => "© OpenStreetMap contributors";

    public void Dispose() => _requestGate.Dispose();

    public async Task<PlaceName?> ReverseGeocodeAsync(
        string picturesRoot,
        GeoCircle area,
        CancellationToken cancellationToken = default)
    {
        var cache = _cacheFactory.Create(picturesRoot);
        await cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var usePointOfInterest = area.RadiusMeters <= 1_000;
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"place-v4|{area.Center.Latitude:0.0000}|{area.Center.Longitude:0.0000}|poi:{usePointOfInterest}");
        var cached = await cache.GetGeocodeAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return JsonSerializer.Deserialize<PlaceName>(cached);
        }

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cachedAfterWaiting = await cache.GetGeocodeAsync(
                cacheKey,
                cancellationToken).ConfigureAwait(false);
            if (cachedAfterWaiting is not null)
            {
                return JsonSerializer.Deserialize<PlaceName>(cachedAfterWaiting);
            }

            var delay = TimeSpan.FromSeconds(1) - (DateTimeOffset.UtcNow - _lastRequestAt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            var builder = new UriBuilder(new Uri(Endpoint, "reverse"))
            {
                Query = string.Create(
                    CultureInfo.InvariantCulture,
                    $"format=jsonv2&lat={area.Center.Latitude:R}&lon={area.Center.Longitude:R}&zoom=18&addressdetails=1&namedetails=1&extratags=1"),
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
            request.Headers.UserAgent.ParseAdd("PhotoSorter/1.0 (local Windows desktop application)");
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _lastRequestAt = DateTimeOffset.UtcNow;
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<NominatimResponse>(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(result?.DisplayName))
            {
                return null;
            }

            var pointOfInterest = usePointOfInterest && IsUsefulPointOfInterest(result, area);
            var placeName = new PlaceName(
                pointOfInterest ? CreatePointOfInterestName(result) : CreateLocalityName(result),
                result.DisplayName,
                pointOfInterest);
            await cache.SetGeocodeAsync(
                cacheKey,
                JsonSerializer.Serialize(placeName),
                cancellationToken).ConfigureAwait(false);
            return placeName;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static bool IsUsefulPointOfInterest(NominatimResponse result, GeoCircle area)
    {
        if (string.IsNullOrWhiteSpace(result.Name))
        {
            return false;
        }

        var category = First(result.Category, result.LegacyClass);
        if (string.IsNullOrWhiteSpace(category)
            || !IsUsefulCategoryAndType(category, result.Type))
        {
            return false;
        }

        if (!double.TryParse(result.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(result.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return false;
        }

        var maximumDistance = Math.Clamp(area.RadiusMeters + 50, 75, 250);
        return GeoMath.DistanceMeters(area.Center, new GeoPoint(latitude, longitude)) <= maximumDistance;
    }

    private static bool IsUsefulCategoryAndType(string category, string? type)
    {
        if (PointOfInterestCategories.Contains(category))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return category.ToLowerInvariant() switch
        {
            "aeroway" => AerowayPointOfInterestTypes.Contains(type),
            "amenity" => AmenityPointOfInterestTypes.Contains(type),
            "building" => BuildingPointOfInterestTypes.Contains(type),
            "railway" => RailwayPointOfInterestTypes.Contains(type),
            _ => false,
        };
    }

    private static string CreatePointOfInterestName(NominatimResponse result)
    {
        var pointOfInterest = result.Name!.Trim();
        var context = First(
            result.Address?.Suburb,
            result.Address?.CityDistrict,
            result.Address?.City,
            result.Address?.Town,
            result.Address?.Village,
            result.Address?.Municipality);
        return string.IsNullOrWhiteSpace(context)
            || string.Equals(pointOfInterest, context, StringComparison.OrdinalIgnoreCase)
            ? pointOfInterest
            : $"{pointOfInterest}, {context}";
    }

    private static string CreateLocalityName(NominatimResponse result)
    {
        var address = result.Address;
        var locality = First(
            address?.City,
            address?.Town,
            address?.Village,
            address?.Municipality,
            address?.County);
        var district = First(address?.Suburb, address?.CityDistrict);

        if (!string.IsNullOrWhiteSpace(district)
            && !string.Equals(district, locality, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(locality)
                ? district
                : $"{district}, {locality}";
        }

        if (!string.IsNullOrWhiteSpace(locality))
        {
            return !string.IsNullOrWhiteSpace(address?.State)
                && !string.Equals(locality, address.State, StringComparison.OrdinalIgnoreCase)
                ? $"{locality}, {address.State}"
                : locality;
        }

        return First(address?.State, address?.Country, result.Name, result.DisplayName)
            ?? result.DisplayName!;
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record NominatimResponse(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("class")] string? LegacyClass,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("lat")] string? Latitude,
        [property: JsonPropertyName("lon")] string? Longitude,
        [property: JsonPropertyName("address")] NominatimAddress? Address);

    private sealed record NominatimAddress(
        [property: JsonPropertyName("suburb")] string? Suburb,
        [property: JsonPropertyName("city_district")] string? CityDistrict,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("town")] string? Town,
        [property: JsonPropertyName("village")] string? Village,
        [property: JsonPropertyName("municipality")] string? Municipality,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("country")] string? Country);
}
