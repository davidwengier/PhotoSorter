using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class PhotoSorterStateValidatorTests
{
    [TestMethod]
    public void Validate_DefaultState_ReturnsNoErrors()
    {
        var state = new PhotoSorterState();

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsEmpty(errors);
    }

    [TestMethod]
    public void Validate_NullState_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => PhotoSorterStateValidator.Validate(null!));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Validate_NonPositiveSchemaVersion_ReturnsError(int schemaVersion)
    {
        var state = new PhotoSorterState { SchemaVersion = schemaVersion };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("positive integer", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_SchemaVersionNewerThanSupported_ReturnsError()
    {
        var state = new PhotoSorterState { SchemaVersion = PhotoSorterState.CurrentSchemaVersion + 1 };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("newer than the supported version", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_NullCollections_ReturnsActionableErrors()
    {
        var state = new PhotoSorterState
        {
            RoutineLocations = null!,
            IgnoredFolders = null!,
            IgnoredGroups = null!,
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("routineLocations", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("ignoredFolders", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(error => error.Contains("ignoredGroups", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_DuplicateRoutineLocationIds_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            RoutineLocations =
            [
                new RoutineLocationDecision { Id = "dup", Name = "Home", Center = new GeoPoint(1, 1) },
                new RoutineLocationDecision { Id = "dup", Name = "Work", Center = new GeoPoint(2, 2) },
            ],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("routineLocations contains duplicate id 'dup'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_DuplicateIgnoredFolderIds_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            IgnoredFolders =
            [
                new IgnoredFolderRule { Id = "dup", RelativePath = "2023/A" },
                new IgnoredFolderRule { Id = "dup", RelativePath = "2023/B" },
            ],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("ignoredFolders contains duplicate id 'dup'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_DuplicateIgnoredGroupIds_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            IgnoredGroups =
            [
                new IgnoredGroupRule { Id = "dup", Areas = [ValidCircle()] },
                new IgnoredGroupRule { Id = "dup", Areas = [ValidCircle()] },
            ],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("ignoredGroups contains duplicate id 'dup'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_RoutineLocationMissingName_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            RoutineLocations = [new RoutineLocationDecision { Id = "r1", Name = "   ", Center = new GeoPoint(1, 1) }],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("must have a name", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(91.0, 0.0, DisplayName = "Latitude above range")]
    [DataRow(-91.0, 0.0, DisplayName = "Latitude below range")]
    [DataRow(0.0, 181.0, DisplayName = "Longitude above range")]
    [DataRow(0.0, -181.0, DisplayName = "Longitude below range")]
    public void Validate_RoutineLocationOutOfRangeCoordinates_ReturnsError(double latitude, double longitude)
    {
        var state = new PhotoSorterState
        {
            RoutineLocations =
            [
                new RoutineLocationDecision { Id = "r1", Name = "Home", Center = new GeoPoint(latitude, longitude) },
            ],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsNotEmpty(errors);
    }

    [TestMethod]
    public void Validate_RoutineLocationNonPositiveRadius_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            RoutineLocations =
            [
                new RoutineLocationDecision { Id = "r1", Name = "Home", Center = new GeoPoint(1, 1), RadiusMeters = 0 },
            ],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("must have a positive radiusMeters", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_IgnoredFolderNonPortablePath_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            IgnoredFolders = [new IgnoredFolderRule { Id = "f1", RelativePath = @"C:\Absolute\Path" }],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("Pictures-root-relative path", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_IgnoredGroupEndBeforeStart_ReturnsError()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new PhotoSorterState
        {
            IgnoredGroups =
            [
                new IgnoredGroupRule { Id = "g1", Start = now, End = now.AddHours(-1), Areas = [ValidCircle()] },
            ],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("end before its start", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_IgnoredGroupNegativeTimePadding_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            IgnoredGroups = [new IgnoredGroupRule { Id = "g1", TimePaddingMinutes = -5, Areas = [ValidCircle()] }],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("negative timePaddingMinutes", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(-0.1)]
    [DataRow(1.1)]
    public void Validate_IgnoredGroupFractionOutOfRange_ReturnsError(double fraction)
    {
        var state = new PhotoSorterState
        {
            IgnoredGroups =
            [
                new IgnoredGroupRule { Id = "g1", RequiredLocationMatchFraction = fraction, Areas = [ValidCircle()] },
            ],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("requiredLocationMatchFraction", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_IgnoredGroupNoAreas_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            IgnoredGroups = [new IgnoredGroupRule { Id = "g1", Areas = [] }],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("at least one geographic area", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_IgnoredGroupAreaNonPositiveRadius_ReturnsError()
    {
        var state = new PhotoSorterState
        {
            IgnoredGroups =
            [
                new IgnoredGroupRule
                {
                    Id = "g1",
                    Areas = [new GeoCircle { Center = new GeoPoint(1, 1), RadiusMeters = 0 }],
                },
            ],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsTrue(errors.Any(error => error.Contains("positive area radii", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Validate_ValidNonDefaultState_ReturnsNoErrors()
    {
        var state = new PhotoSorterState
        {
            RoutineLocations = [new RoutineLocationDecision { Id = "r1", Name = "Home", Center = new GeoPoint(10, 20), RadiusMeters = 100 }],
            IgnoredFolders = [new IgnoredFolderRule { Id = "f1", RelativePath = @"2023\Screenshots" }],
            IgnoredGroups = [new IgnoredGroupRule { Id = "g1", Areas = [ValidCircle()] }],
        };

        var errors = PhotoSorterStateValidator.Validate(state);

        Assert.IsEmpty(errors);
    }

    private static GeoCircle ValidCircle() => new() { Center = new GeoPoint(1, 1), RadiusMeters = 100 };
}
