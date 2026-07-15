using Microsoft.Extensions.Logging.Abstractions;
using PhotoSorter.Infrastructure.Cache;
using PhotoSorter.Infrastructure.Tests.TestSupport;

namespace PhotoSorter.Infrastructure.Tests;

[TestClass]
public sealed class PreviewWindowPlacementStoreTests
{
    [TestMethod]
    public void Load_MissingFile_ReturnsNullWithoutCreatingFile()
    {
        using var temp = new TempDirectory();
        var sut = CreateStore(temp.Path);

        var placement = sut.Load();

        Assert.IsNull(placement);
        Assert.IsEmpty(Directory.GetFiles(temp.Path));
    }

    [TestMethod]
    public void SaveThenLoad_ValidPlacement_RoundTrips()
    {
        using var temp = new TempDirectory();
        var sut = CreateStore(temp.Path);
        var expected = new PreviewWindowPlacement
        {
            Left = -120.5,
            Top = 64.25,
            Width = 1_280,
            Height = 860,
            IsMaximized = true,
        };

        sut.Save(expected);
        var actual = sut.Load();

        Assert.AreEqual(expected, actual);
        Assert.HasCount(1, Directory.GetFiles(temp.Path));
    }

    [TestMethod]
    public void Load_MalformedFile_ReturnsNullWithoutChangingFile()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "preview-window.json");
        const string malformed = "{ this is not valid json ";
        File.WriteAllText(path, malformed);
        var sut = CreateStore(temp.Path);

        var placement = sut.Load();

        Assert.IsNull(placement);
        Assert.AreEqual(malformed, File.ReadAllText(path));
    }

    private static PreviewWindowPlacementStore CreateStore(string cacheBasePath) =>
        new(cacheBasePath, NullLogger<PreviewWindowPlacementStore>.Instance);
}
