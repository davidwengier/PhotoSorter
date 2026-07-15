using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;
using PhotoSorter.Core.Contracts;
using PhotoSorter.Infrastructure.Cache;

namespace PhotoSorter.Infrastructure.Thumbnails;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsThumbnailService(CachePathProvider cachePathProvider) : IThumbnailService
{
    private const long MaximumCacheBytes = 512L * 1024 * 1024;

    private readonly string _cachePath = Path.Combine(cachePathProvider.BasePath, "thumbnails");
    private int _writesSinceTrim;

    public async Task<byte[]?> GetThumbnailAsync(
        string absolutePath,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        int pixelSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (pixelSize is < 32 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelSize));
        }

        var cacheFile = GetCacheFile(absolutePath, length, lastWriteTimeUtc, pixelSize);
        if (File.Exists(cacheFile))
        {
            var cached = await File.ReadAllBytesAsync(cacheFile, cancellationToken).ConfigureAwait(false);
            File.SetLastAccessTimeUtc(cacheFile, DateTime.UtcNow);
            return cached;
        }

        var bytes = await Task.Run(
            () => CreateThumbnail(absolutePath, pixelSize),
            cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        Directory.CreateDirectory(_cachePath);
        var temporaryFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryFile, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryFile, cacheFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }

        if (Interlocked.Increment(ref _writesSinceTrim) >= 100)
        {
            Interlocked.Exchange(ref _writesSinceTrim, 0);
            await Task.Run(TrimCache, cancellationToken).ConfigureAwait(false);
        }

        return bytes;
    }

    private byte[]? CreateThumbnail(string absolutePath, int pixelSize)
    {
        IShellItemImageFactory? factory = null;
        try
        {
            var interfaceId = typeof(IShellItemImageFactory).GUID;
            NativeMethods.SHCreateItemFromParsingName(
                absolutePath,
                0,
                ref interfaceId,
                out factory);
            var result = factory.GetImage(
                new NativeSize(pixelSize, pixelSize),
                ShellImageFlags.BiggerSizeOk | ShellImageFlags.ThumbnailOnly,
                out var bitmapHandle);
            Marshal.ThrowExceptionForHR(result);

            using var safeBitmap = new SafeHBitmapHandle(bitmapHandle);
            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                safeBitmap.DangerousGetHandle(),
                0,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception exception) when (exception is COMException or ExternalException or IOException)
        {
            return null;
        }
        finally
        {
            if (factory is not null)
            {
                Marshal.FinalReleaseComObject(factory);
            }
        }
    }

    private string GetCacheFile(
        string absolutePath,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        int pixelSize)
    {
        var key = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Path.GetFullPath(absolutePath).ToUpperInvariant()}|{length}|{lastWriteTimeUtc.UtcTicks}|{pixelSize}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_cachePath, Convert.ToHexString(hash).ToLowerInvariant() + ".png");
    }

    private void TrimCache()
    {
        if (!Directory.Exists(_cachePath))
        {
            return;
        }

        var files = new DirectoryInfo(_cachePath)
            .EnumerateFiles("*.png")
            .OrderBy(static file => file.LastAccessTimeUtc)
            .ToArray();
        var total = files.Sum(static file => file.Length);
        foreach (var file in files)
        {
            if (total <= MaximumCacheBytes)
            {
                break;
            }

            try
            {
                total -= file.Length;
                file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, ShellImageFlags flags, out nint bitmapHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize(int width, int height)
    {
        public readonly int Width = width;

        public readonly int Height = height;
    }

    [Flags]
    private enum ShellImageFlags : uint
    {
        BiggerSizeOk = 0x00000001,
        ThumbnailOnly = 0x00000008,
    }

    private sealed class SafeHBitmapHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeHBitmapHandle(nint handle)
            : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() => NativeMethods.DeleteObject(handle) != 0;
    }

    private static partial class NativeMethods
    {
        // Runtime COM interface marshalling is required for this Shell API.
        [DllImport(
            "shell32.dll",
            EntryPoint = "SHCreateItemFromParsingName",
            CharSet = CharSet.Unicode,
            PreserveSig = false)]
        internal static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            nint bindingContext,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
        internal static partial int DeleteObject(nint handle);
    }
}
