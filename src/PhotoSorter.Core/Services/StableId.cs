using System.Security.Cryptography;
using System.Text;

namespace PhotoSorter.Core.Services;

internal static class StableId
{
    public static string Create(string prefix, IEnumerable<string> values)
    {
        var payload = string.Join('\n', values.Order(StringComparer.OrdinalIgnoreCase));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"{prefix}-{Convert.ToHexString(hash.AsSpan(0, 10)).ToLowerInvariant()}";
    }
}
