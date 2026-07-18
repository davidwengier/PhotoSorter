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
        Assert.IsEmpty(state.IgnoredGroups);
        Assert.IsFalse(File.Exists(sut.GetStatePath(temp.Path)));
    }

    [TestMethod]
    public async Task UpdateAsync_NewRoot_WritesVersionTwoWithoutRemovedFields()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        await CreateStateFileAsync(sut, temp.Path);

        var content = await File.ReadAllTextAsync(sut.GetStatePath(temp.Path));
        Assert.IsTrue(content.Contains("\"schemaVersion\": 2", StringComparison.Ordinal));
        Assert.IsTrue(content.Contains("\n  \"schemaVersion\"", StringComparison.Ordinal));
        Assert.IsTrue(content.Contains("\"ignoredGroups\": []", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("\"ignoredFolders\"", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("\"disposition\"", StringComparison.Ordinal));
        Assert.IsFalse(content.Contains("\"suppressCandidates\"", StringComparison.Ordinal));
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
    public async Task UpdateAsync_AddsDecisionsWithoutChangingExistingEntries()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        var updated = await sut.UpdateAsync(
            temp.Path,
            state => state with
            {
                RoutineLocations =
                [
                    .. state.RoutineLocations,
                    new RoutineLocationDecision
                    {
                        Id = "home",
                        Name = "Home",
                        Center = new GeoPoint(1.5, 2.5),
                        RadiusMeters = 400,
                    },
                ],
            });

        Assert.HasCount(1, updated.RoutineLocations);
        Assert.IsEmpty(updated.IgnoredGroups);

        var second = await sut.UpdateAsync(
            temp.Path,
            state => state with
            {
                IgnoredGroups = [.. state.IgnoredGroups, CreateIgnoredGroup("g1")],
            });

        Assert.HasCount(1, second.RoutineLocations);
        Assert.AreEqual("home", second.RoutineLocations[0].Id);
        Assert.HasCount(1, second.IgnoredGroups);
        Assert.AreEqual(PhotoSorterState.CurrentSchemaVersion, second.SchemaVersion);
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
            "\"schemaVersion\": 2",
            "\"schemaVersion\": 999",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(statePath, futureContent);

        await Assert.ThrowsExactlyAsync<StateFileException>(() => sut.LoadAsync(temp.Path));

        Assert.AreEqual(futureContent, await File.ReadAllTextAsync(statePath));
    }

    [TestMethod]
    public async Task LoadAsync_CurrentSchema_DoesNotRewriteFile()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        var statePath = sut.GetStatePath(temp.Path);
        const string current =
            """
            {
              // Keep this comment and formatting when no migration is needed.
              "schemaVersion": 2,
              "routineLocations": [],
              "ignoredGroups": []
            }
            """;
        await File.WriteAllTextAsync(statePath, current);

        var state = await sut.LoadAsync(temp.Path);

        Assert.AreEqual(PhotoSorterState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.AreEqual(current, await File.ReadAllTextAsync(statePath));
    }

    [TestMethod]
    public async Task LoadAsync_VersionOneState_RewritesVersionTwoAndDropsObsoleteData()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        var statePath = sut.GetStatePath(temp.Path);
        const string versionOne =
            """
            {
              "schemaVersion": 1,
              "routineLocations": [
                {
                  "id": "home",
                  "name": "Home",
                  "disposition": "routine",
                  "center": { "latitude": 1.5, "longitude": 2.5 },
                  "radiusMeters": 400,
                  "suppressCandidates": true
                },
                {
                  "id": "not-routine",
                  "name": "Not routine",
                  "disposition": "notRoutine",
                  "center": { "latitude": 3.0, "longitude": 4.0 },
                  "radiusMeters": 500,
                  "suppressCandidates": true
                },
                {
                  "id": "disabled",
                  "name": "Disabled",
                  "disposition": "routine",
                  "center": { "latitude": 5.0, "longitude": 6.0 },
                  "radiusMeters": 500,
                  "suppressCandidates": false
                }
              ],
              "ignoredFolders": [
                {
                  "id": "private",
                  "relativePath": "2023/Phone Images/Private",
                  "recursive": true,
                  "label": "Private"
                }
              ],
              "ignoredGroups": [
                {
                  "id": "handled",
                  "label": "Already handled",
                  "kind": "event",
                  "start": "2023-06-01T10:00:00+00:00",
                  "end": "2023-06-01T11:00:00+00:00",
                  "timePaddingMinutes": 90,
                  "requiredLocationMatchFraction": 0.5,
                  "areas": [
                    {
                      "center": { "latitude": 1.0, "longitude": 2.0 },
                      "radiusMeters": 500
                    }
                  ]
                }
              ],
              "preferences": {
                "reverseGeocodingEnabled": true
              }
            }
            """;
        await File.WriteAllTextAsync(statePath, versionOne);

        var state = await sut.LoadAsync(temp.Path);

        Assert.AreEqual(PhotoSorterState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.HasCount(1, state.RoutineLocations);
        Assert.AreEqual("home", state.RoutineLocations[0].Id);
        Assert.HasCount(1, state.IgnoredGroups);
        Assert.AreEqual("handled", state.IgnoredGroups[0].Id);

        var migrated = await File.ReadAllTextAsync(statePath);
        Assert.IsTrue(migrated.Contains("\"schemaVersion\": 2", StringComparison.Ordinal));
        Assert.IsTrue(migrated.Contains("\"label\": \"Already handled\"", StringComparison.Ordinal));
        Assert.IsFalse(migrated.Contains("\"ignoredFolders\"", StringComparison.Ordinal));
        Assert.IsFalse(migrated.Contains("\"disposition\"", StringComparison.Ordinal));
        Assert.IsFalse(migrated.Contains("\"suppressCandidates\"", StringComparison.Ordinal));
        Assert.IsFalse(migrated.Contains("\"preferences\"", StringComparison.Ordinal));
        Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
        Assert.IsFalse(File.Exists(statePath + ".lock"));
    }

    [TestMethod]
    public async Task LoadAsync_VersionOneStateWithOnlyObsoleteData_DeletesFile()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        var statePath = sut.GetStatePath(temp.Path);
        const string versionOne =
            """
            {
              "schemaVersion": 1,
              "routineLocations": [
                {
                  "id": "disabled",
                  "name": "Disabled",
                  "disposition": "routine",
                  "center": { "latitude": 5.0, "longitude": 6.0 },
                  "radiusMeters": 500,
                  "suppressCandidates": false
                }
              ],
              "ignoredFolders": [
                {
                  "id": "private",
                  "relativePath": "2023/Phone Images/Private",
                  "recursive": true,
                  "label": null
                }
              ],
              "ignoredGroups": [],
              "preferences": {
                "reverseGeocodingEnabled": true
              }
            }
            """;
        await File.WriteAllTextAsync(statePath, versionOne);

        var state = await sut.LoadAsync(temp.Path);

        Assert.IsEmpty(state.RoutineLocations);
        Assert.IsEmpty(state.IgnoredGroups);
        Assert.IsFalse(File.Exists(statePath));
        Assert.IsFalse(File.Exists(statePath + ".lock"));
    }

    [TestMethod]
    public async Task LoadAsync_VersionOneDefaultsWithoutSchemaOrFlags_PreservesRoutineLocation()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();
        var statePath = sut.GetStatePath(temp.Path);
        const string versionOne =
            """
            {
              "routineLocations": [
                {
                  "id": "home",
                  "name": "Home",
                  "center": { "latitude": 1.5, "longitude": 2.5 },
                  "radiusMeters": 400
                }
              ],
              "ignoredGroups": []
            }
            """;
        await File.WriteAllTextAsync(statePath, versionOne);

        var state = await sut.LoadAsync(temp.Path);

        Assert.AreEqual(PhotoSorterState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.HasCount(1, state.RoutineLocations);
        Assert.AreEqual("home", state.RoutineLocations[0].Id);
        Assert.IsTrue(
            (await File.ReadAllTextAsync(statePath))
            .Contains("\"schemaVersion\": 2", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task UpdateAsync_NoDecisions_DoesNotCreateFile()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        var state = await sut.UpdateAsync(temp.Path, static current => current);

        Assert.IsEmpty(state.RoutineLocations);
        Assert.IsEmpty(state.IgnoredGroups);
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
            current => current with { RoutineLocations = [] });

        Assert.IsEmpty(state.RoutineLocations);
        Assert.IsFalse(File.Exists(sut.GetStatePath(temp.Path)));
    }

    [TestMethod]
    public async Task UpdateAsync_SuccessfulUpdate_LeavesNoTemporaryFilesBehind()
    {
        using var temp = new TempDirectory();
        var sut = new JsonSharedStateStore();

        await CreateStateFileAsync(sut, temp.Path);

        Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
        Assert.IsFalse(File.Exists(sut.GetStatePath(temp.Path) + ".lock"));
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
                    RoutineLocations =
                    [
                        .. state.RoutineLocations,
                        new RoutineLocationDecision
                        {
                            Id = "invalid",
                            Name = "Invalid",
                            Center = new GeoPoint(1, 1),
                            RadiusMeters = 0,
                        },
                    ],
                }));

        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(statePath));
        Assert.IsEmpty(Directory.GetFiles(temp.Path, "*.tmp"));
        Assert.IsFalse(File.Exists(statePath + ".lock"));
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
                RoutineLocations =
                [
                    .. state.RoutineLocations,
                    new RoutineLocationDecision
                    {
                        Id = "a",
                        Name = "RootA-Home",
                        Center = new GeoPoint(1, 1),
                    },
                ],
            });
        await sut.UpdateAsync(
            tempB.Path,
            state => state with
            {
                RoutineLocations =
                [
                    .. state.RoutineLocations,
                    new RoutineLocationDecision
                    {
                        Id = "b",
                        Name = "RootB-Home",
                        Center = new GeoPoint(2, 2),
                    },
                ],
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

    private static IgnoredGroupRule CreateIgnoredGroup(string id) =>
        new()
        {
            Id = id,
            Label = "Handled",
            Kind = CandidateKind.Event,
            Start = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2023, 6, 1, 11, 0, 0, TimeSpan.Zero),
            TimePaddingMinutes = 90,
            RequiredLocationMatchFraction = 0.5,
            Areas = [new GeoCircle { Center = new GeoPoint(1, 2), RadiusMeters = 500 }],
        };

    private static Task<PhotoSorterState> CreateStateFileAsync(
        JsonSharedStateStore store,
        string root) =>
        store.UpdateAsync(
            root,
            state => state with
            {
                RoutineLocations =
                [
                    .. state.RoutineLocations,
                    new RoutineLocationDecision
                    {
                        Id = "home",
                        Name = "Home",
                        Center = new GeoPoint(1.5, 2.5),
                        RadiusMeters = 400,
                    },
                ],
            });
}
