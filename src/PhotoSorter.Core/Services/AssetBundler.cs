using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Services;

public sealed class AssetBundler
{
    public IReadOnlyList<AssetBundle> Bundle(IReadOnlyList<MediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        var sets = new DisjointSet(assets.Count);

        foreach (var group in assets
                     .Select((asset, index) => (asset, index))
                     .Where(static item => !string.IsNullOrWhiteSpace(item.asset.ContentIdentifier))
                     .GroupBy(
                         static item => item.asset.ContentIdentifier!,
                         StringComparer.OrdinalIgnoreCase))
        {
            UnionGroup(group.Select(static item => item.index), sets);
        }

        foreach (var group in assets
                     .Select((asset, index) => (asset, index))
                     .GroupBy(
                         static item => Path.Combine(
                             item.asset.DirectoryRelativePath,
                             item.asset.BaseName),
                         StringComparer.OrdinalIgnoreCase))
        {
            var materialized = group.ToArray();
            var hasPrimary = materialized.Any(
                static item => item.asset.Kind is MediaKind.Image or MediaKind.Video);
            var hasCompanion = materialized.Any(
                static item => item.asset.Kind is MediaKind.Sidecar)
                || (materialized.Any(static item => item.asset.Kind == MediaKind.Image)
                    && materialized.Any(static item => item.asset.Kind == MediaKind.Video));

            if (hasPrimary && hasCompanion)
            {
                UnionGroup(materialized.Select(static item => item.index), sets);
            }
        }

        return assets
            .Select((asset, index) => (asset, root: sets.Find(index)))
            .GroupBy(static item => item.root)
            .Select(group =>
            {
                var bundledAssets = group
                    .Select(static item => item.asset)
                    .OrderBy(static asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var id = StableId.Create(
                    "bundle",
                    bundledAssets.Select(static asset => asset.RelativePath));
                return new AssetBundle(id, bundledAssets);
            })
            .OrderBy(static bundle => bundle.CapturedAt)
            .ThenBy(static bundle => bundle.PrimaryAsset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void UnionGroup(IEnumerable<int> indexes, DisjointSet sets)
    {
        using var enumerator = indexes.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return;
        }

        var first = enumerator.Current;
        while (enumerator.MoveNext())
        {
            sets.Union(first, enumerator.Current);
        }
    }

    private sealed class DisjointSet(int count)
    {
        private readonly int[] _parents = Enumerable.Range(0, count).ToArray();
        private readonly byte[] _ranks = new byte[count];

        public int Find(int value)
        {
            if (_parents[value] != value)
            {
                _parents[value] = Find(_parents[value]);
            }

            return _parents[value];
        }

        public void Union(int first, int second)
        {
            var firstRoot = Find(first);
            var secondRoot = Find(second);
            if (firstRoot == secondRoot)
            {
                return;
            }

            if (_ranks[firstRoot] < _ranks[secondRoot])
            {
                _parents[firstRoot] = secondRoot;
            }
            else if (_ranks[firstRoot] > _ranks[secondRoot])
            {
                _parents[secondRoot] = firstRoot;
            }
            else
            {
                _parents[secondRoot] = firstRoot;
                _ranks[firstRoot]++;
            }
        }
    }
}
