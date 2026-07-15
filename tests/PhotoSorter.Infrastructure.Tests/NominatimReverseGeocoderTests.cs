using System.Net;
using System.Text;
using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Models;
using PhotoSorter.Infrastructure.Geocoding;
using PhotoSorter.Infrastructure.Tests.TestSupport;

namespace PhotoSorter.Infrastructure.Tests;

[TestClass]
public sealed class NominatimReverseGeocoderTests
{
    [TestMethod]
    public async Task ReverseGeocodeAsync_SuburbAndCity_ReturnsConcisePlaceName()
    {
        const string responseJson =
            """
            {
              "display_name": "Docklands, City of Melbourne, Victoria, Australia",
              "address": {
                "suburb": "Docklands",
                "city": "Melbourne",
                "state": "Victoria",
                "country": "Australia"
              }
            }
            """;

        var result = await ReverseGeocodeAsync(responseJson, radiusMeters: 300);

        Assert.AreEqual("Docklands, Melbourne", result.Place.ShortName);
        Assert.IsFalse(result.Place.IsPointOfInterest);
        Assert.AreEqual(
            "Docklands, City of Melbourne, Victoria, Australia",
            result.Place.DisplayName);
        Assert.IsTrue(result.RequestUri.Query.Contains("addressdetails=1", StringComparison.Ordinal));
        Assert.IsTrue(result.RequestUri.Query.Contains("namedetails=1", StringComparison.Ordinal));
        Assert.IsTrue(result.RequestUri.Query.Contains("zoom=18", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ReverseGeocodeAsync_NearbyNamedPoiForCompactGroup_PrefersPoiAndSuburb()
    {
        const string responseJson =
            """
            {
              "display_name": "Melbourne Zoo, Elliott Avenue, Parkville, Melbourne, Victoria, Australia",
              "name": "Melbourne Zoo",
              "category": "tourism",
              "type": "zoo",
              "lat": "-37.8141",
              "lon": "144.9634",
              "address": {
                "suburb": "Parkville",
                "city": "Melbourne",
                "state": "Victoria",
                "country": "Australia"
              }
            }
            """;

        var result = await ReverseGeocodeAsync(responseJson, radiusMeters: 400);

        Assert.AreEqual("Melbourne Zoo, Parkville", result.Place.ShortName);
        Assert.IsTrue(result.Place.IsPointOfInterest);
    }

    [TestMethod]
    public async Task ReverseGeocodeAsync_NamedPoiForBroadArea_UsesLocalityInstead()
    {
        const string responseJson =
            """
            {
              "display_name": "Melbourne Zoo, Elliott Avenue, Parkville, Melbourne, Victoria, Australia",
              "name": "Melbourne Zoo",
              "category": "tourism",
              "type": "zoo",
              "lat": "-37.8141",
              "lon": "144.9634",
              "address": {
                "suburb": "Parkville",
                "city": "Melbourne",
                "state": "Victoria",
                "country": "Australia"
              }
            }
            """;

        var result = await ReverseGeocodeAsync(responseJson, radiusMeters: 2_500);

        Assert.AreEqual("Parkville, Melbourne", result.Place.ShortName);
        Assert.IsFalse(result.Place.IsPointOfInterest);
    }

    [TestMethod]
    public async Task ReverseGeocodeAsync_RoadName_IsNotTreatedAsPoi()
    {
        const string responseJson =
            """
            {
              "display_name": "Swanston Street, Melbourne, Victoria, Australia",
              "name": "Swanston Street",
              "category": "highway",
              "type": "primary",
              "address": {
                "city": "Melbourne",
                "state": "Victoria",
                "country": "Australia"
              }
            }
            """;

        var result = await ReverseGeocodeAsync(responseJson, radiusMeters: 100);

        Assert.AreEqual("Melbourne, Victoria", result.Place.ShortName);
        Assert.IsFalse(result.Place.IsPointOfInterest);
    }

    [TestMethod]
    public async Task ReverseGeocodeAsync_DistantNamedPoi_UsesLocalityInstead()
    {
        const string responseJson =
            """
            {
              "display_name": "Melbourne Zoo, Parkville, Melbourne, Victoria, Australia",
              "name": "Melbourne Zoo",
              "category": "tourism",
              "type": "zoo",
              "lat": "-37.7840",
              "lon": "144.9510",
              "address": {
                "suburb": "Parkville",
                "city": "Melbourne",
                "state": "Victoria"
              }
            }
            """;

        var result = await ReverseGeocodeAsync(responseJson, radiusMeters: 200);

        Assert.AreEqual("Parkville, Melbourne", result.Place.ShortName);
        Assert.IsFalse(result.Place.IsPointOfInterest);
    }

    [TestMethod]
    public async Task ReverseGeocodeAsync_NearbyShop_IsNotTreatedAsDestinationPoi()
    {
        const string responseJson =
            """
            {
              "display_name": "Example Shop, Docklands, Melbourne, Victoria, Australia",
              "name": "Example Shop",
              "category": "shop",
              "type": "department_store",
              "lat": "-37.8141",
              "lon": "144.9634",
              "address": {
                "suburb": "Docklands",
                "city": "Melbourne",
                "state": "Victoria"
              }
            }
            """;

        var result = await ReverseGeocodeAsync(responseJson, radiusMeters: 100);

        Assert.AreEqual("Docklands, Melbourne", result.Place.ShortName);
        Assert.IsFalse(result.Place.IsPointOfInterest);
    }

    [TestMethod]
    public async Task ReverseGeocodeAsync_RepeatedLocation_UsesStructuredCache()
    {
        using var temp = new TempDirectory();
        var handler = new StubHttpHandler(
            """
            {
              "display_name": "Geelong, Victoria, Australia",
              "address": { "city": "Geelong", "state": "Victoria" }
            }
            """);
        using var client = new HttpClient(handler);
        var cache = new FakeMediaCache();
        using var sut = new NominatimReverseGeocoder(
            client,
            new FakeMediaCacheFactory(cache));
        var area = Area(-38.1499, 144.3617, 500);

        var first = await sut.ReverseGeocodeAsync(temp.Path, area);
        var second = await sut.ReverseGeocodeAsync(temp.Path, area);

        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(first, second);
        Assert.AreEqual("Geelong, Victoria", second?.ShortName);
    }

    private static async Task<(PlaceName Place, Uri RequestUri)> ReverseGeocodeAsync(
        string responseJson,
        double radiusMeters)
    {
        using var temp = new TempDirectory();
        var handler = new StubHttpHandler(responseJson);
        using var client = new HttpClient(handler);
        using var sut = new NominatimReverseGeocoder(
            client,
            new FakeMediaCacheFactory(new FakeMediaCache()));

        var place = await sut.ReverseGeocodeAsync(
            temp.Path,
            Area(-37.814, 144.9633, radiusMeters));

        Assert.IsNotNull(place);
        Assert.IsNotNull(handler.LastRequestUri);
        return (place, handler.LastRequestUri);
    }

    private static GeoCircle Area(
        double latitude,
        double longitude,
        double radiusMeters) =>
        new()
        {
            Center = new GeoPoint(latitude, longitude),
            RadiusMeters = radiusMeters,
        };

    private sealed class StubHttpHandler(string responseJson) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeMediaCacheFactory(FakeMediaCache cache) : IMediaCacheFactory
    {
        public IMediaCache Create(string picturesRoot) => cache;
    }

    private sealed class FakeMediaCache : IMediaCache
    {
        private readonly Dictionary<string, string> _geocodes = new(StringComparer.Ordinal);

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, MediaAsset>> LoadAssetsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, MediaAsset>>(
                new Dictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase));

        public Task ReplaceAssetsAsync(
            IReadOnlyCollection<MediaAsset> assets,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string?> GetGeocodeAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _geocodes.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task SetGeocodeAsync(
            string key,
            string displayName,
            CancellationToken cancellationToken = default)
        {
            _geocodes[key] = displayName;
            return Task.CompletedTask;
        }
    }
}
