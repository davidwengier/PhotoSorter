using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class PathRuleMatcherTests
{
    [TestMethod]
    public void NormalizeRelativePath_AltSeparators_ConvertsToPlatformSeparator()
    {
        var result = PathRuleMatcher.NormalizeRelativePath("2023/Phone Images/photo.jpg");

        Assert.AreEqual(@"2023\Phone Images\photo.jpg", result);
    }

    [TestMethod]
    public void NormalizeRelativePath_LeadingAndTrailingSeparatorsAndWhitespace_Trimmed()
    {
        var result = PathRuleMatcher.NormalizeRelativePath(@"  \2023\Phone Images\ ");

        Assert.AreEqual(@"2023\Phone Images", result);
    }

    [TestMethod]
    public void NormalizeRelativePath_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PathRuleMatcher.NormalizeRelativePath(null!));
    }

    [TestMethod]
    [DataRow(@"2023\Phone Images\photo.jpg", true, DisplayName = "Relative nested path")]
    [DataRow(@"C:\2023\Phone Images\photo.jpg", false, DisplayName = "Rooted path")]
    [DataRow(@"2023\..\Phone Images\photo.jpg", false, DisplayName = "Contains parent segment")]
    [DataRow(@".\2023\photo.jpg", false, DisplayName = "Contains current-dir segment")]
    [DataRow("", false, DisplayName = "Empty path")]
    [DataRow("   ", false, DisplayName = "Whitespace path")]
    public void IsPortableRelativePath_VariousInputs_ReturnsExpected(string path, bool expected)
    {
        var result = PathRuleMatcher.IsPortableRelativePath(path);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void IsUnderPhoneImages_MatchingYearPrefix_ReturnsTrue()
    {
        var result = PathRuleMatcher.IsUnderPhoneImages(@"2023\Phone Images\photo.jpg", 2023);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsUnderPhoneImages_DifferentYear_ReturnsFalse()
    {
        var result = PathRuleMatcher.IsUnderPhoneImages(@"2023\Phone Images\photo.jpg", 2022);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsUnderPhoneImages_MissingPhoneImagesSegment_ReturnsFalse()
    {
        var result = PathRuleMatcher.IsUnderPhoneImages(@"2023\Screenshots\photo.jpg", 2023);

        Assert.IsFalse(result);
    }
}
