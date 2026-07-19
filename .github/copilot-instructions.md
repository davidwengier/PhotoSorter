# PhotoSorter repository instructions

## Product invariants

PhotoSorter is a Windows-only .NET 10 WPF application that reviews media under `Pictures\<year>\Phone Images`. It treats the selected Pictures root as an ordinary filesystem folder; do not add OneDrive APIs or machine-specific assumptions.

- Never move files automatically. A user must select the bundles, choose a destination, and confirm the final plan.
- Never edit media metadata or overwrite destination files.
- Keep linked image, video, and sidecar files together. `AssetBundle`, not `MediaAsset`, is the atomic review and move unit.
- Never use a real photo library in tests. Use generated data and unique temporary directories.

## Build, run, test, and publish

Run commands from the repository root in PowerShell. The solution uses `.slnx`, central package management, and no `global.json`.

```powershell
dotnet restore .\PhotoSorter.slnx
dotnet build .\PhotoSorter.slnx --no-restore
dotnet run --project .\src\PhotoSorter.App\PhotoSorter.App.csproj
dotnet test .\PhotoSorter.slnx
dotnet format .\PhotoSorter.slnx --verify-no-changes --no-restore
.\scripts\publish.ps1
```

The publish script creates a self-contained `win-x64` build in `artifacts\publish\win-x64`.

Tests use MSTest with VSTest-style filters. To run one test:

```powershell
dotnet test .\tests\PhotoSorter.Infrastructure.Tests\PhotoSorter.Infrastructure.Tests.csproj --filter "FullyQualifiedName=PhotoSorter.Infrastructure.Tests.FileSystemMoveExecutorTests.ExecuteAsync_MixedDestinationConflictAndMove_SkipsConflictAndMovesRest"
```

## Architecture and runtime flow

- `PhotoSorter.Core` targets `net10.0` and has no WPF or Infrastructure dependency. It owns domain models, service contracts, bundling, grouping, decision matching, candidate editing, path rules, validation, stable IDs, and move planning.
- `PhotoSorter.Infrastructure` implements filesystem scanning, metadata extraction, SQLite caching, reverse geocoding, thumbnails, JSON state, logging, window placement, and move execution.
- `PhotoSorter.App` is the WPF composition/UI layer. `App.xaml.cs` registers services with the Generic Host; `MainViewModel` coordinates the workflow using Core and Infrastructure abstractions.

Put deterministic rules in Core, I/O behind contracts in Infrastructure, and UI state/commands in App. Register new services in `App.xaml.cs`; do not put reusable grouping, persistence, or file-operation logic in window code-behind.

The main flow is:

1. `Program.Main` runs the Velopack startup hook before creating WPF.
2. `MainWindow` initializes `MainViewModel` and checks for updates once at startup.
3. `MainViewModel` loads shared state, scans the root, groups bundles, and starts place naming.
4. `MediaScanner` recursively discovers supported files only below four-digit year folders containing `Phone Images`, reuses matching cache fingerprints, extracts metadata, and calls `AssetBundler`.
5. `GroupingEngine` creates per-year event/trip candidates, attaches suitable time-only bundles, and applies saved routine-location and ignored-group decisions.
6. Review edits and merge/destination drafts remain in memory. Explicit ignore actions update shared state.
7. `MovePlanner` builds an immutable plan; the executor preflights it, the UI confirms it, execution runs, and the library is rescanned.

## Persistence boundaries

`Pictures\.photosorter.json` is the only authoritative cross-machine state. Schema version 2 contains only:

- `routineLocations`
- `ignoredGroups`

Do not persist candidate drafts, selections, automatic place names, recent roots, move history, completed moves, or unanswered prompts there. Apart from schema migration during load, the file is created or changed only after an explicit durable decision and is deleted when no decisions remain.

Shared-state updates must preserve `JsonSharedStateStore` semantics: validate strictly, reject unsupported/newer schemas, re-read under a short-lived lock, migrate old schemas, write a flushed temporary sibling, and atomically replace the file. Schema changes require model, validator, migration, fixture/test, and README updates.

Everything under `%LocalAppData%\PhotoSorter\Cache` is disposable and machine-local: SQLite metadata/geocode data, thumbnails, logs, recent-root hints, and window placement. Never make correct behavior or user decisions depend only on this cache.

Use Pictures-root-relative paths and Windows case-insensitive comparisons for portable state and plans. Do not store drive letters in shared state.

## Grouping and geocoding

- Keep tunable grouping thresholds in `GroupingOptions`; update deterministic `GroupingEngineTests` when behavior changes.
- Low-confidence timestamps are not GPS anchors. Medium/high-confidence time-only bundles may be attached when one candidate is clearly closest.
- Candidate and bundle IDs are stable hashes of their member IDs/relative paths. Preserve stable ordering before generating IDs.
- The review list sorts by confidence descending, then newest first.
- Nominatim receives only approximate candidate centers. Requests must remain serialized, limited to one per second, attributed to OpenStreetMap, and cached locally.

## Move safety

Move safety is a core product contract:

- Sources must remain under `<year>\Phone Images`; destinations must be a direct child of the same four-digit year and must not be `Phone Images`.
- Plans carry expected source length and exact UTC modified time. Revalidate immediately before each operation.
- Never overwrite a destination.
- Files are considered equivalent only when their case-insensitive filename, byte length, and exact `LastWriteTimeUtc` match. Deleting an equivalent source requires explicit confirmation; recheck equivalence immediately before deletion.
- A non-equivalent same-name destination skips the entire linked bundle while unrelated bundles continue.
- Missing/changed sources, invalid paths, and real I/O failures stop the remaining batch. Report moved, deleted, skipped, failed, and unattempted entries exactly.
- There is intentionally no move history, undo, rollback, or crash-recovery journal.

Add or update `FileSystemMoveExecutorTests` and `MovePlannerTests` for any change to these rules.

## WPF and MVVM conventions

- Use CommunityToolkit.Mvvm `[ObservableProperty]` and `[RelayCommand]`. Notify dependent computed properties from generated partial change handlers.
- Keep code-behind limited to window-specific interactions such as opening dialogs, preview windows, or Explorer.
- Getter-only properties bound through WPF `Run.Text` require explicit `Mode=OneWay`; otherwise WPF may attempt a TwoWay binding and fail at runtime.
- WPF construction/layout regression tests use `[STATestMethod]`. Use `[DoNotParallelize]` when a test shares `Application.Current`.
- Do not drive the live desktop with UI automation; construct and exercise windows in-process.

## Code and test conventions

- `Directory.Build.props` enables nullable references, latest analyzers/language features, build-time code-style enforcement, and warnings as errors.
- Package versions belong in `Directory.Packages.props`; omit `Version` from individual `PackageReference` items.
- Follow `.editorconfig`: CRLF, four-space indentation, System usings first, file-scoped namespaces, and ordinary constructors preferred for new code.
- Test names use `Method_Scenario_Expected`. Test projects suppress CA1707 for this convention.
- All test assemblies enable method-level parallelization, so tests must isolate files and mutable state.
- MSTest analyzer suggestions are build errors. Prefer `Assert.IsEmpty`, `Assert.IsNotEmpty`, and `Assert.HasCount` over assertions on `collection.Count`.

## Releases and updates

Every push to `main` runs tests and publishes a GitHub Release through `.github\workflows\release.yml`. CI derives the version as `0.<github.run_number>.0`; do not manually bump the source `<Version>` for routine releases.

Installed Velopack builds check and download updates at startup. Development builds launched with `dotnet run` skip update checks. Keep the Velopack startup hook before WPF initialization.
