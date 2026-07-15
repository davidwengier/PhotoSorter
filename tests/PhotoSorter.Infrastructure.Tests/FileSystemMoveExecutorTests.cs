using PhotoSorter.Core.Models;
using PhotoSorter.Infrastructure.Moves;
using PhotoSorter.Infrastructure.Tests.TestSupport;

namespace PhotoSorter.Infrastructure.Tests;

[TestClass]
public sealed class FileSystemMoveExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_ValidPlan_MovesAllFilesAndReportsMoved()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "content-a");
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var result = await sut.ExecuteAsync(plan);

        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(1, result.Items);
        Assert.AreEqual(MoveItemStatus.Moved, result.Items[0].Status);
        Assert.IsFalse(File.Exists(sourcePath));
        Assert.IsTrue(File.Exists(Path.Combine(temp.Path, "2023", "Trip", "a.jpg")));
        Assert.AreEqual("content-a", await File.ReadAllTextAsync(Path.Combine(temp.Path, "2023", "Trip", "a.jpg")));
    }

    [TestMethod]
    public async Task PreflightAsync_DestinationAlreadyExists_ReportsError()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "source");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        await File.WriteAllTextAsync(Path.Combine(destinationDir, "a.jpg"), "already-here");
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsFalse(preflight.IsValid);
        Assert.IsTrue(preflight.Errors.Any(error => error.Contains("Destination already exists", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ExecuteAsync_DestinationAlreadyExists_FailsWithoutOverwritingAndLeavesSourceInPlace()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "source");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var destinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(destinationPath, "already-here");
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var result = await sut.ExecuteAsync(plan);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(MoveItemStatus.Failed, result.Items[0].Status);
        Assert.IsTrue(File.Exists(sourcePath));
        Assert.AreEqual("already-here", await File.ReadAllTextAsync(destinationPath));
    }

    [TestMethod]
    public async Task PreflightAsync_SourceFileSizeChanged_ReportsSizeChangedError()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "short");
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        await File.WriteAllTextAsync(sourcePath, "this content is now much longer than expected");
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsFalse(preflight.IsValid);
        Assert.IsTrue(preflight.Errors.Any(error => error.Contains("Source file size changed", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PreflightAsync_SourceFileTimestampChanged_ReportsTimestampChangedError()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "content");
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddDays(-3));
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsFalse(preflight.IsValid);
        Assert.IsTrue(preflight.Errors.Any(error => error.Contains("Source file timestamp changed", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PreflightAsync_SourceFileMissing_ReportsNoLongerExistsError()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images"));
        var missingSourcePath = Path.Combine(temp.Path, "2023", "Phone Images", "missing.jpg");
        var entry = CreateEntry(missingSourcePath, "bundle-a", @"2023\Phone Images\missing.jpg", @"2023\Trip\missing.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsFalse(preflight.IsValid);
        Assert.IsTrue(preflight.Errors.Any(error => error.Contains("Source file no longer exists", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PreflightAsync_DestinationYearDiffersFromSource_ReportsError()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "content");
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2024\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2024\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsFalse(preflight.IsValid);
        Assert.IsTrue(preflight.Errors.Any(error => error.Contains("Destination year does not match", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PreflightAsync_DestinationInsidePhoneImages_ReportsError()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "content");
        var entry = CreateEntry(
            sourcePath,
            "bundle-a",
            @"2023\Phone Images\a.jpg",
            @"2023\Phone Images\Nested\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Phone Images", [entry]);
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsFalse(preflight.IsValid);
        Assert.IsTrue(preflight.Errors.Any(error => error.Contains("cannot be Phone Images", StringComparison.Ordinal)));
        Assert.IsTrue(preflight.Errors.Any(error => error.Contains("Destination cannot be inside Phone Images", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PreflightAsync_EntryOutsidePlannedDestinationFolder_ReportsError()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "content");
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Other\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsFalse(preflight.IsValid);
        Assert.IsTrue(preflight.Errors.Any(error => error.Contains("outside the planned destination folder", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ExecuteAsync_MiddleFileLocked_ReportsExactPartialFailureAndStopsRemaining()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var pathA = Path.Combine(sourceDir, "a.jpg");
        var pathB = Path.Combine(sourceDir, "b.jpg");
        var pathC = Path.Combine(sourceDir, "c.jpg");
        await File.WriteAllTextAsync(pathA, "content-a");
        await File.WriteAllTextAsync(pathB, "content-b");
        await File.WriteAllTextAsync(pathC, "content-c");
        var entryA = CreateEntry(pathA, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var entryB = CreateEntry(pathB, "bundle-b", @"2023\Phone Images\b.jpg", @"2023\Trip\b.jpg");
        var entryC = CreateEntry(pathC, "bundle-c", @"2023\Phone Images\c.jpg", @"2023\Trip\c.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entryA, entryB, entryC]);
        var sut = new FileSystemMoveExecutor();

        using (new FileStream(pathB, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await sut.ExecuteAsync(plan);

            Assert.HasCount(3, result.Items);
            Assert.AreEqual(MoveItemStatus.Moved, result.Items[0].Status);
            Assert.AreEqual(MoveItemStatus.Failed, result.Items[1].Status);
            Assert.IsNotNull(result.Items[1].Error);
            Assert.AreEqual(MoveItemStatus.NotAttempted, result.Items[2].Status);
            Assert.IsFalse(result.Succeeded);
        }

        Assert.IsFalse(File.Exists(pathA));
        Assert.IsTrue(File.Exists(pathB));
        Assert.IsTrue(File.Exists(pathC));
    }

    private static MovePlanEntry CreateEntry(
        string sourceAbsolutePath,
        string bundleId,
        string sourceRelativePath,
        string destinationRelativePath)
    {
        var info = new FileInfo(sourceAbsolutePath);
        return new MovePlanEntry
        {
            BundleId = bundleId,
            SourceRelativePath = sourceRelativePath,
            DestinationRelativePath = destinationRelativePath,
            ExpectedLength = info.Exists ? info.Length : 0,
            ExpectedLastWriteTimeUtc = info.Exists
                ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
                : DateTimeOffset.UtcNow,
        };
    }

    private static MovePlan CreatePlan(
        string picturesRoot,
        string destinationDirectoryRelativePath,
        IReadOnlyList<MovePlanEntry> entries) => new()
        {
            PicturesRoot = picturesRoot,
            DestinationDirectoryRelativePath = destinationDirectoryRelativePath,
            Entries = entries,
        };
}
