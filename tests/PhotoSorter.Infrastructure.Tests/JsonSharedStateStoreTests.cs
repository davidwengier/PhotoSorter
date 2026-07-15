using PhotoSorter.Core.Models;
using PhotoSorter.Infrastructure.State;
using PhotoSorter.Infrastructure.Tests.TestSupport;

namespace PhotoSorter.Infrastructure.Tests;

[TestClass]
public sealed class JsonSharedStateStoreTests
{
    [TestMethod]
    public async Task LoadAsync_NewRoot_ReturnsDefaultWithoutCreatingFile()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        var state = await sut.LoadAsync(temp.Path);

        Assert.AreEqual(PhotoSorterState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.IsEmpty(state.RoutineLocations);
        Assert.IsFalse(File.Exists(sut.GetStatePath(temp.Path)));
    }

    [TestMethod]
    public async Task UpdateAsync_NewRoot_WritesIndentedCamelCaseJson()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        await CreateStateFileAsync(sut, temp.Path);

        var content = await File.ReadAllTextAsync(sut.GetStatePath(temp.Path));
        Assert.IsTrue(content.Contains("\"schemaVersion\": 1", StringComparison.Ordinal));
        Assert.IsTrue(content.Contains("\n  \"schemaVersion\"", StringComparison.Ordinal));
        Assert.IsTrue(content.Contains("\"routineLocations\": []", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("\"preferences\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task UpdateAsync_TwoRootsWithSameDecision_ProduceByteIdenticalFiles()
    {
        using var tempA = new TempDirectory();
        using var tempB = new TempDirectory();
        var sut = new JsonSharedStateStore();

        await CreateStateFileAsync(sut, tempA.Path);
        await CreateStateFileAsync(sut, tempB.Path);

        var bytesA = await File.ReadAllBytesAsync(sut.GetStatePath(tempA.Path));
        var bytesB = await File.ReadAllBytesAsync(sut.GetStatePath(tempB.Path));
        CollectionAssert.AreEqual(bytesA, bytesB);
    }

    [TestMethod]
    public async Task UpdateAsync_AddsOneRoutineLocation_OnlyThatFieldChanges()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        var updated = await sut.UpdateAsync(
            temp.Path,
            state => state with
            {
                RoutineLocations = [.. state.RoutineLocations, new RoutineLocationDecision
                {
                    Id = "home",
                    Name = "Home",
                    Center = new GeoPoint(1.5, 2.5),
                    RadiusMeters = 400,
                }],
            });

        Assert.HasCount(1, updated.RoutineLocations);
        Assert.IsEmpty(updated.IgnoredFolders);
        Assert.IsEmpty(updated.IgnoredGroups);
        Assert.AreEqual(PhotoSorterState.CurrentSchemaVersion, updated.SchemaVersion);
        Assert.IsNull(updated.LegacyPreferences);

        var second = await sut.UpdateAsync(
            temp.Path,
            state => state with
            {
                IgnoredFolders = [.. state.IgnoredFolders, new IgnoredFolderRule
                {
                    Id = "f1",
                    RelativePath = "2023/Screenshots",
                }],
            });

        Assert.HasCount(1, second.RoutineLocations);
        Assert.AreEqual("home", second.RoutineLocations[0].Id);
        Assert.HasCount(1, second.IgnoredFolders);
    }

    [TestMethod]
    public async Task LoadAsync_MalformedJsonFile_ThrowsAndLeavesFileUnchanged()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        var statePath = sut.GetStatePath(temp.Path);
        const string malformed = "{ this is not valid json ";
        await File.WriteAllTextAsync(statePath, malformed);

        await Assert.ThrowsExactlyAsync<StateFileException>(() => sut.LoadAsync(temp.Path));

        Assert.AreEqual(malformed, await File.ReadAllTextAsync(statePath));
    }

    [TestMethod]
    public async Task LoadAsync_FutureSchemaVersion_ThrowsAndLeavesFileUnchanged()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        await CreateStateFileAsync(sut, temp.Path);
        var statePath = sut.GetStatePath(temp.Path);
        var original = await File.ReadAllTextAsync(statePath);
        var futureContent = original.Replace(
            "\"schemaVersion\": 1",
            "\"schemaVersion\": 999",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(statePath, futureContent);

        await Assert.ThrowsExactlyAsync<StateFileException>(() => sut.LoadAsync(temp.Path));

        Assert.AreEqual(futureContent, await File.ReadAllTextAsync(statePath));
    }

    [TestMethod]
    public async Task UpdateAsync_RootedIgnoredFolderPath_ThrowsAndRejectsAsNonPortable()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        await Assert.ThrowsExactlyAsync<StateFileException>(
            () => sut.UpdateAsync(
                temp.Path,
                state => state with
                {
                    IgnoredFolders = [.. state.IgnoredFolders, new IgnoredFolderRule
                    {
                        Id = "f1",
                        RelativePath = @"C:\Absolute\Path",
                    }],
                }));
        Assert.IsFalse(File.Exists(sut.GetStatePath(temp.Path)));
    }

    [TestMethod]
    public async Task UpdateAsync_RelativeIgnoredFolderPath_Succeeds()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        var updated = await sut.UpdateAsync(
            temp.Path,
            state => state with
            {
                IgnoredFolders = [.. state.IgnoredFolders, new IgnoredFolderRule
                {
                    Id = "f1",
                    RelativePath = @"2023\Screenshots",
                }],
            });

        Assert.HasCount(1, updated.IgnoredFolders);
        Assert.IsTrue(File.Exists(sut.GetStatePath(temp.Path)));
    }

    [TestMethod]
    public async Task UpdateAsync_NoDecisions_DoesNotCreateFile()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        var state = await sut.UpdateAsync(temp.Path, static current => current);

        Assert.IsEmpty(state.RoutineLocations);
        Assert.IsFalse(File.Exists(sut.GetStatePath(temp.Path)));
    }

    [TestMethod]
    public async Task UpdateAsync_RemovingLastDecision_DeletesFile()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        await CreateStateFileAsync(sut, temp.Path);

        var state = await sut.UpdateAsync(
            temp.Path,
            current => current with { IgnoredFolders = [] });

        Assert.IsEmpty(state.IgnoredFolders);
        Assert.IsFalse(File.Exists(sut.GetStatePath(temp.Path)));
    }

    [TestMethod]
    public async Task LoadAsync_LegacyPreferencesOnlyFile_IsAcceptedWithoutChangingIt()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        var statePath = sut.GetStatePath(temp.Path);
        const string legacy =
            """
            {
              "schemaVersion": 1,
              "routineLocations": [],
              "ignoredFolders": [],
              "ignoredGroups": [],
              "preferences": {
                "reverseGeocodingEnabled": true,
                "reverseGeocodingProvider": "nominatim",
                "reverseGeocodingEndpoint": "https://nominatim.openstreetmap.org/"
              }
            }
            """;
        await File.WriteAllTextAsync(statePath, legacy);

        var state = await sut.LoadAsync(temp.Path);

        Assert.IsNull(state.LegacyPreferences);
        Assert.AreEqual(legacy, await File.ReadAllTextAsync(statePath));
    }

    [TestMethod]
    public async Task UpdateAsync_SuccessfulUpdate_LeavesNoTemporaryFilesBehind()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        await CreateStateFileAsync(sut, temp.Path);

        Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [TestMethod]
    public async Task UpdateAsync_FailedValidation_LeavesOriginalFileUntouchedAndNoTemporaryFiles()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        await CreateStateFileAsync(sut, temp.Path);
        var statePath = sut.GetStatePath(temp.Path);
        var before = await File.ReadAllBytesAsync(statePath);

        await Assert.ThrowsExactlyAsync<StateFileException>(
            () => sut.UpdateAsync(
                temp.Path,
                state => state with
                {
                    IgnoredFolders = [.. state.IgnoredFolders, new IgnoredFolderRule
                    {
                        Id = "f1",
                        RelativePath = @"..\Escape",
                    }],
                }));

        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(statePath));
        Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [TestMethod]
    public async Task UpdateAsync_TwoIndependentRoots_DoNotCrossContaminateState()
    {
        using var tempA = new TempDirectory();
        using var tempB = new TempDirectory();
        var sut = new JsonSharedStateStore();

        await sut.UpdateAsync(
            tempA.Path,
            state => state with
            {
                RoutineLocations = [.. state.RoutineLocations, new RoutineLocationDecision
                {
                    Id = "a",
                    Name = "RootA-Home",
                    Center = new GeoPoint(1, 1),
                }],
            });
        await sut.UpdateAsync(
            tempB.Path,
            state => state with
            {
                RoutineLocations = [.. state.RoutineLocations, new RoutineLocationDecision
                {
                    Id = "b",
                    Name = "RootB-Home",
                    Center = new GeoPoint(2, 2),
                }],
            });

        var stateA = await sut.LoadAsync(tempA.Path);
        var stateB = await sut.LoadAsync(tempB.Path);

        Assert.HasCount(1, stateA.RoutineLocations);
        Assert.AreEqual("RootA-Home", stateA.RoutineLocations[0].Name);
        Assert.HasCount(1, stateB.RoutineLocations);
        Assert.AreEqual("RootB-Home", stateB.RoutineLocations[0].Name);
    }

    [TestMethod]
    public void GetStatePath_NonExistentRoot_Throws()
    {
        var sut = new JsonSharedStateStore();
        var missingRoot = Path.Combine(Path.GetTempPath(), "DoesNotExist_" + Guid.NewGuid().ToString("N"));

        Assert.ThrowsExactly<DirectoryNotFoundException>(() => sut.GetStatePath(missingRoot));
    }

    private static Task<PhotoSorterState> CreateStateFileAsync(
        JsonSharedStateStore store,
        string root) =>
        store.UpdateAsync(
            root,
            state => state with
            {
                IgnoredFolders =
                [
                    .. state.IgnoredFolders,
                    new IgnoredFolderRule
                    {
                        Id = "screenshots",
                        RelativePath = "2023/Screenshots",
                    },
                ],
            });
}
