# PhotoSorter

PhotoSorter is a local Windows desktop app for reviewing photo groups found in:

```text
Pictures\<year>\Phone Images
```

It clusters photos and linked media using capture time and GPS metadata. Suggestions are shown in confidence order and named automatically before you select them. Nothing moves until you review a group, name its destination, and confirm the move.

## Key behavior

- Finds fixed-location events and multi-stop trips.
- Reads JPG, JPEG, HEIC/HEIF, PNG, WebP, GIF, DNG, AVIF, JP2, common videos, XMP, and NAR files.
- Keeps linked images, videos, and sidecars together.
- Attaches suitable time-adjacent media that has no GPS.
- Lets you ignore one suggestion or always ignore its named location directly from the review screen.
- Opens any thumbnail in a large photo viewer with previous/next navigation and a synchronized include checkbox.
- Switches each photo group between a detailed list and an image-only grid.
- Remembers the large viewer's size, position, and maximized state in the disposable machine-local cache.
- Supports new or existing year-level destination folders.
- Never overwrites an existing file.
- Stops at the first move failure and reports moved, failed, and unattempted files.
- Has no move history, undo, rollback, or crash-recovery system.

## Shared decisions

The only authoritative PhotoSorter state is:

```text
Pictures\.photosorter.json
```

It is indented, versioned JSON designed to be readable and editable by hand. It stores only explicit ignore decisions. Scanning, automatic place naming, viewing photos, or editing an unfinished suggestion does not create or update the file. Removing the final saved decision removes the now-empty file.

All other data is disposable and machine-local:

```text
%LocalAppData%\PhotoSorter\Cache
```

That cache contains extracted metadata, thumbnails, reverse-geocode responses, diagnostics, and the recent-root hint. Deleting it does not lose any explicit answers.

PhotoSorter treats the selected root as an ordinary folder. It has no OneDrive-specific APIs or behavior, so the same synced folder can be used from different machines and drive letters.

## Automatic place and POI names

PhotoSorter names suggestions automatically through OpenStreetMap's Nominatim service. Compact groups prefer a credible nearby attraction, venue, school, park, station, or landmark; broader groups use suburb, city, or region names. Multi-place groups use their first and last locations.

Only approximate group centers are sent. Photos are never uploaded. Requests are single-threaded, limited to one per second, attributed to OpenStreetMap contributors, and cached locally.

## Build and run

Requirements:

- Windows 10 version 2004 or newer.
- .NET 10 SDK for development.

```powershell
dotnet build .\PhotoSorter.slnx
dotnet run --project .\src\PhotoSorter.App\PhotoSorter.App.csproj
```

Run the test suite:

```powershell
dotnet test .\PhotoSorter.slnx
```

Create a self-contained Windows x64 build:

```powershell
.\scripts\publish.ps1
```

The published app is written to `artifacts\publish\win-x64`.

## First use

1. Choose the Pictures folder containing the four-digit year folders.
2. Let the metadata scan complete; this does not create `.photosorter.json`.
3. Place and POI names fill in automatically from highest to lowest confidence.
4. Review a suggestion in list or grid view, use the large viewer for full photo details, and untick anything that does not belong.
5. Choose **Ignore this one**, **Ignore this location always**, or edit the suggested folder name and choose **Name and move**.

Use disposable copies when first evaluating move behavior. The application never edits photo metadata or automatically downloads inaccessible files.
