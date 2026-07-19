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
    public async Task PreflightAsync_DestinationAlreadyExists_ReturnsConflict()
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

        Assert.IsTrue(preflight.IsValid);
        Assert.IsEmpty(preflight.Errors);
        Assert.IsEmpty(preflight.EquivalentDestinationEntries);
        Assert.HasCount(1, preflight.ConflictingDestinationEntries);
        Assert.AreSame(entry, preflight.ConflictingDestinationEntries[0]);
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
    public async Task PreflightAsync_EquivalentDestination_ReturnsSourceDeletionCandidate()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "same-content");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var destinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(destinationPath, "same-content");
        SetMatchingWriteTimes(sourcePath, destinationPath);
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsTrue(preflight.IsValid);
        Assert.HasCount(1, preflight.EquivalentDestinationEntries);
        Assert.AreSame(entry, preflight.EquivalentDestinationEntries[0]);
    }

    [TestMethod]
    public async Task PreflightAsync_SameNameAndSizeWithDifferentTimestamp_ReturnsConflict()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "same-content");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var destinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(destinationPath, "same-content");
        var sourceTime = new DateTime(2023, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, sourceTime);
        File.SetLastWriteTimeUtc(destinationPath, sourceTime.AddSeconds(1));
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);

        Assert.IsTrue(preflight.IsValid);
        Assert.IsEmpty(preflight.Errors);
        Assert.IsEmpty(preflight.EquivalentDestinationEntries);
        Assert.HasCount(1, preflight.ConflictingDestinationEntries);
        Assert.AreSame(entry, preflight.ConflictingDestinationEntries[0]);
    }

    [TestMethod]
    public async Task ExecuteAsync_EquivalentDestinationWithoutApproval_LeavesSourceInPlace()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "same-content");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var destinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(destinationPath, "same-content");
        SetMatchingWriteTimes(sourcePath, destinationPath);
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var result = await sut.ExecuteAsync(plan);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(MoveItemStatus.Failed, result.Items[0].Status);
        Assert.IsTrue(File.Exists(sourcePath));
        Assert.IsTrue(File.Exists(destinationPath));
    }

    [TestMethod]
    public async Task ExecuteAsync_EquivalentDestinationWithApproval_DeletesOnlySource()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "same-content");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var destinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(destinationPath, "same-content");
        SetMatchingWriteTimes(sourcePath, destinationPath);
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var result = await sut.ExecuteAsync(
            plan,
            new MoveExecutionOptions { DeleteEquivalentSources = true });

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(MoveItemStatus.DeletedEquivalentSource, result.Items[0].Status);
        Assert.IsFalse(File.Exists(sourcePath));
        Assert.IsTrue(File.Exists(destinationPath));
        Assert.AreEqual("same-content", await File.ReadAllTextAsync(destinationPath));
    }

    [TestMethod]
    public async Task ExecuteAsync_DestinationConflictWithSkipApproval_LeavesSourceInPlace()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var sourcePath = Path.Combine(sourceDir, "a.jpg");
        await File.WriteAllTextAsync(sourcePath, "source");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var destinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(destinationPath, "different-destination");
        var entry = CreateEntry(sourcePath, "bundle-a", @"2023\Phone Images\a.jpg", @"2023\Trip\a.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [entry]);
        var sut = new FileSystemMoveExecutor();

        var result = await sut.ExecuteAsync(
            plan,
            new MoveExecutionOptions { SkipDestinationConflicts = true });

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(MoveItemStatus.SkippedDestinationConflict, result.Items[0].Status);
        Assert.IsTrue(File.Exists(sourcePath));
        Assert.AreEqual("different-destination", await File.ReadAllTextAsync(destinationPath));
    }

    [TestMethod]
    public async Task ExecuteAsync_MixedDestinationConflictAndMove_SkipsConflictAndMovesRest()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var conflictSourcePath = Path.Combine(sourceDir, "a.jpg");
        var movedSourcePath = Path.Combine(sourceDir, "b.jpg");
        await File.WriteAllTextAsync(conflictSourcePath, "source-a");
        await File.WriteAllTextAsync(movedSourcePath, "source-b");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var conflictDestinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(conflictDestinationPath, "different-a");
        var conflictEntry = CreateEntry(
            conflictSourcePath,
            "bundle-a",
            @"2023\Phone Images\a.jpg",
            @"2023\Trip\a.jpg");
        var movedEntry = CreateEntry(
            movedSourcePath,
            "bundle-b",
            @"2023\Phone Images\b.jpg",
            @"2023\Trip\b.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [conflictEntry, movedEntry]);
        var sut = new FileSystemMoveExecutor();

        var result = await sut.ExecuteAsync(
            plan,
            new MoveExecutionOptions { SkipDestinationConflicts = true });

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(MoveItemStatus.SkippedDestinationConflict, result.Items[0].Status);
        Assert.AreEqual(MoveItemStatus.Moved, result.Items[1].Status);
        Assert.IsTrue(File.Exists(conflictSourcePath));
        Assert.IsFalse(File.Exists(movedSourcePath));
        Assert.AreEqual("different-a", await File.ReadAllTextAsync(conflictDestinationPath));
        Assert.AreEqual(
            "source-b",
            await File.ReadAllTextAsync(Path.Combine(destinationDir, "b.jpg")));
    }

    [TestMethod]
    public async Task ExecuteAsync_OneFileInBundleConflicts_SkipsEntireLinkedBundle()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var imageSourcePath = Path.Combine(sourceDir, "a.jpg");
        var sidecarSourcePath = Path.Combine(sourceDir, "a.xmp");
        await File.WriteAllTextAsync(imageSourcePath, "source-image");
        await File.WriteAllTextAsync(sidecarSourcePath, "source-sidecar");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var imageDestinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(imageDestinationPath, "different-image");
        var imageEntry = CreateEntry(
            imageSourcePath,
            "linked-bundle",
            @"2023\Phone Images\a.jpg",
            @"2023\Trip\a.jpg");
        var sidecarEntry = CreateEntry(
            sidecarSourcePath,
            "linked-bundle",
            @"2023\Phone Images\a.xmp",
            @"2023\Trip\a.xmp");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [imageEntry, sidecarEntry]);
        var sut = new FileSystemMoveExecutor();

        var preflight = await sut.PreflightAsync(plan);
        var result = await sut.ExecuteAsync(
            plan,
            new MoveExecutionOptions { SkipDestinationConflicts = true });

        Assert.HasCount(2, preflight.ConflictingDestinationEntries);
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Items.All(static item =>
            item.Status == MoveItemStatus.SkippedDestinationConflict));
        Assert.IsTrue(File.Exists(imageSourcePath));
        Assert.IsTrue(File.Exists(sidecarSourcePath));
        Assert.IsFalse(File.Exists(Path.Combine(destinationDir, "a.xmp")));
    }

    [TestMethod]
    public async Task ExecuteAsync_MixedMoveAndEquivalentDestination_CompletesBothActions()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var duplicateSourcePath = Path.Combine(sourceDir, "a.jpg");
        var movedSourcePath = Path.Combine(sourceDir, "b.jpg");
        await File.WriteAllTextAsync(duplicateSourcePath, "same-content");
        await File.WriteAllTextAsync(movedSourcePath, "new-content");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var duplicateDestinationPath = Path.Combine(destinationDir, "a.jpg");
        await File.WriteAllTextAsync(duplicateDestinationPath, "same-content");
        SetMatchingWriteTimes(duplicateSourcePath, duplicateDestinationPath);
        var duplicateEntry = CreateEntry(
            duplicateSourcePath,
            "bundle-a",
            @"2023\Phone Images\a.jpg",
            @"2023\Trip\a.jpg");
        var movedEntry = CreateEntry(
            movedSourcePath,
            "bundle-b",
            @"2023\Phone Images\b.jpg",
            @"2023\Trip\b.jpg");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [duplicateEntry, movedEntry]);
        var sut = new FileSystemMoveExecutor();

        var result = await sut.ExecuteAsync(
            plan,
            new MoveExecutionOptions { DeleteEquivalentSources = true });

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(MoveItemStatus.DeletedEquivalentSource, result.Items[0].Status);
        Assert.AreEqual(MoveItemStatus.Moved, result.Items[1].Status);
        Assert.IsFalse(File.Exists(duplicateSourcePath));
        Assert.IsFalse(File.Exists(movedSourcePath));
        Assert.IsTrue(File.Exists(duplicateDestinationPath));
        Assert.AreEqual(
            "new-content",
            await File.ReadAllTextAsync(Path.Combine(destinationDir, "b.jpg")));
    }

    [TestMethod]
    public async Task ExecuteAsync_EquivalentDestinationChangesAfterPreflight_LeavesSourceInPlace()
    {
        using var temp = new TempDirectory();
        var sourceDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var movedSourcePath = Path.Combine(sourceDir, "a.jpg");
        var duplicateSourcePath = Path.Combine(sourceDir, "b.jpg");
        var linkedSourcePath = Path.Combine(sourceDir, "b.xmp");
        await File.WriteAllTextAsync(movedSourcePath, "new-content");
        await File.WriteAllTextAsync(duplicateSourcePath, "same-content");
        await File.WriteAllTextAsync(linkedSourcePath, "linked-content");
        var destinationDir = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Trip")).FullName;
        var duplicateDestinationPath = Path.Combine(destinationDir, "b.jpg");
        await File.WriteAllTextAsync(duplicateDestinationPath, "same-content");
        SetMatchingWriteTimes(duplicateSourcePath, duplicateDestinationPath);
        var movedEntry = CreateEntry(
            movedSourcePath,
            "bundle-a",
            @"2023\Phone Images\a.jpg",
            @"2023\Trip\a.jpg");
        var duplicateEntry = CreateEntry(
            duplicateSourcePath,
            "bundle-b",
            @"2023\Phone Images\b.jpg",
            @"2023\Trip\b.jpg");
        var linkedEntry = CreateEntry(
            linkedSourcePath,
            "bundle-b",
            @"2023\Phone Images\b.xmp",
            @"2023\Trip\b.xmp");
        var plan = CreatePlan(temp.Path, @"2023\Trip", [movedEntry, duplicateEntry, linkedEntry]);
        var changedWriteTime = File.GetLastWriteTimeUtc(duplicateDestinationPath).AddMinutes(1);
        var progress = new CallbackProgress<int>(
            _ => File.SetLastWriteTimeUtc(duplicateDestinationPath, changedWriteTime));
        var sut = new FileSystemMoveExecutor();

        var result = await sut.ExecuteAsync(
            plan,
            new MoveExecutionOptions
            {
                DeleteEquivalentSources = true,
                SkipDestinationConflicts = true,
            },
            progress);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(MoveItemStatus.Moved, result.Items[0].Status);
        Assert.AreEqual(MoveItemStatus.SkippedDestinationConflict, result.Items[1].Status);
        Assert.AreEqual(MoveItemStatus.SkippedDestinationConflict, result.Items[2].Status);
        Assert.IsTrue(File.Exists(duplicateSourcePath));
        Assert.IsTrue(File.Exists(linkedSourcePath));
        Assert.IsTrue(File.Exists(duplicateDestinationPath));
        Assert.IsFalse(File.Exists(Path.Combine(destinationDir, "b.xmp")));
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

    private static void SetMatchingWriteTimes(string sourcePath, string destinationPath)
    {
        var writeTime = new DateTime(2023, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, writeTime);
        File.SetLastWriteTimeUtc(destinationPath, writeTime);
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
