namespace PhotoSorter.Infrastructure.Tests.TestSupport;

/// <summary>
/// Creates a unique temporary directory for a single test and deletes it (recursively, best-effort) on dispose.
/// Never touches any real Pictures folder — every fixture lives under <see cref="Path.GetTempPath"/>.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PhotoSorterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
