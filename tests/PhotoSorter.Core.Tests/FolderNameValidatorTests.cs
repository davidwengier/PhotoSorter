using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class FolderNameValidatorTests
{
    [TestMethod]
    [DataRow(null, DisplayName = "Null")]
    [DataRow("", DisplayName = "Empty")]
    [DataRow("   ", DisplayName = "Whitespace")]
    public void Validate_MissingName_ReturnsSingleError(string? folderName)
    {
        var errors = FolderNameValidator.Validate(folderName);

        Assert.HasCount(1, errors);
        Assert.AreEqual("Enter a destination folder name.", errors[0]);
    }

    [TestMethod]
    public void Validate_LeadingWhitespace_ReturnsError()
    {
        var errors = FolderNameValidator.Validate(" Birthday Party");

        Assert.IsTrue(errors.Any(error => error.Contains("cannot start or end with whitespace", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_TrailingWhitespace_ReturnsError()
    {
        var errors = FolderNameValidator.Validate("Birthday Party ");

        Assert.IsTrue(errors.Any(error => error.Contains("cannot start or end with whitespace", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_TrailingPeriod_ReturnsError()
    {
        var errors = FolderNameValidator.Validate("Birthday Party.");

        Assert.IsTrue(errors.Any(error => error.Contains("cannot end with a period", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_NameLongerThan100Characters_ReturnsError()
    {
        var errors = FolderNameValidator.Validate(new string('a', 101));

        Assert.IsTrue(errors.Any(error => error.Contains("100 characters", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_NameOfExactly100Characters_DoesNotReturnLengthError()
    {
        var errors = FolderNameValidator.Validate(new string('a', 100));

        Assert.IsFalse(errors.Any(error => error.Contains("100 characters", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_InvalidFileNameCharacter_ReturnsError()
    {
        var errors = FolderNameValidator.Validate("Trip:2023");

        Assert.IsTrue(errors.Any(error => error.Contains("Windows does not allow", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("Phone Images")]
    [DataRow("phone images")]
    [DataRow("PHONE IMAGES")]
    public void Validate_PhoneImagesLiteral_ReturnsError(string folderName)
    {
        var errors = FolderNameValidator.Validate(folderName);

        Assert.IsTrue(errors.Any(error => error.Contains("'Phone Images' cannot be used", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("CON")]
    [DataRow("con")]
    [DataRow("COM1")]
    [DataRow("LPT9")]
    [DataRow("NUL.txt")]
    public void Validate_ReservedWindowsName_ReturnsError(string folderName)
    {
        var errors = FolderNameValidator.Validate(folderName);

        Assert.IsTrue(errors.Any(error => error.Contains("is a reserved Windows name", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_ValidFolderName_ReturnsNoErrors()
    {
        var errors = FolderNameValidator.Validate("Grandma's 80th Birthday");

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void Validate_NameContainingReservedWordButNotWholeSegment_ReturnsNoReservedError()
    {
        var errors = FolderNameValidator.Validate("Concert Night");

        Assert.IsFalse(errors.Any(error => error.Contains("is a reserved Windows name", StringComparison.Ordinal)));
    }
}
