using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Services;

public sealed class CandidateEditor
{
    public IReadOnlyList<CandidateGroup> FindMergeCandidates(
        CandidateGroup candidate,
        IEnumerable<CandidateGroup> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(other => other.Year == candidate.Year
                && !string.Equals(other.Id, candidate.Id, StringComparison.Ordinal))
            .OrderBy(other => TemporalGap(candidate, other))
            .ThenBy(other => other.Start < candidate.Start)
            .ThenBy(static other => other.Start)
            .ThenBy(static other => other.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public CandidateGroup SelectBundles(
        CandidateGroup candidate,
        IEnumerable<string> includedBundleIds)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(includedBundleIds);

        var included = includedBundleIds.ToHashSet(StringComparer.Ordinal);
        var bundles = candidate.Bundles.Where(bundle => included.Contains(bundle.Id)).ToArray();
        if (bundles.Length == 0)
        {
            throw new ArgumentException("At least one bundle must remain selected.", nameof(includedBundleIds));
        }

        return Rebuild(candidate, bundles, candidate.Areas);
    }

    public (CandidateGroup Before, CandidateGroup After) Split(
        CandidateGroup candidate,
        DateTimeOffset boundary)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var before = candidate.Bundles.Where(bundle => bundle.CapturedAt < boundary).ToArray();
        var after = candidate.Bundles.Where(bundle => bundle.CapturedAt >= boundary).ToArray();
        if (before.Length == 0 || after.Length == 0)
        {
            throw new ArgumentException("The split must leave at least one bundle on each side.", nameof(boundary));
        }

        return (
            Rebuild(candidate, before, CreateAreas(before)),
            Rebuild(candidate, after, CreateAreas(after)));
    }

    public CandidateGroup Merge(CandidateGroup first, CandidateGroup second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.Year != second.Year)
        {
            throw new ArgumentException("Candidates from different years cannot be merged.", nameof(second));
        }

        var bundles = first.Bundles
            .Concat(second.Bundles)
            .DistinctBy(static bundle => bundle.Id)
            .OrderBy(static bundle => bundle.CapturedAt)
            .ToArray();
        var kind = first.Kind == CandidateKind.Trip || second.Kind == CandidateKind.Trip
            ? CandidateKind.Trip
            : CandidateKind.Event;
        var template = first with
        {
            Kind = kind,
            Reasons = first.Reasons.Concat(second.Reasons).Append("Merged manually.").Distinct().ToArray(),
            Score = (first.Score + second.Score) / 2,
        };
        return Rebuild(template, bundles, first.Areas.Concat(second.Areas).ToArray());
    }

    public CandidateGroup AddBundles(
        CandidateGroup candidate,
        IEnumerable<AssetBundle> additionalBundles)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(additionalBundles);

        var bundles = candidate.Bundles
            .Concat(additionalBundles)
            .Where(bundle => bundle.Year == candidate.Year)
            .DistinctBy(static bundle => bundle.Id)
            .OrderBy(static bundle => bundle.CapturedAt)
            .ToArray();
        return Rebuild(
            candidate with
            {
                Reasons = candidate.Reasons.Append("Expanded manually with nearby unassigned items.").ToArray(),
            },
            bundles,
            candidate.Areas);
    }

    private static CandidateGroup Rebuild(
        CandidateGroup template,
        IReadOnlyList<AssetBundle> bundles,
        IReadOnlyList<GeoCircle> areas)
    {
        var ordered = bundles.OrderBy(static bundle => bundle.CapturedAt).ToArray();
        return template with
        {
            Id = StableId.Create(
                template.Kind == CandidateKind.Trip ? "trip" : "event",
                ordered.Select(static bundle => bundle.Id)),
            Start = ordered[0].CapturedAt,
            End = ordered[^1].CapturedAt,
            Bundles = ordered,
            Areas = areas,
        };
    }

    private static TimeSpan TemporalGap(CandidateGroup first, CandidateGroup second)
    {
        if (second.End < first.Start)
        {
            return first.Start - second.End;
        }

        return second.Start > first.End
            ? second.Start - first.End
            : TimeSpan.Zero;
    }

    private static IReadOnlyList<GeoCircle> CreateAreas(IReadOnlyList<AssetBundle> bundles)
    {
        var points = bundles
            .Where(static bundle => bundle.Location is not null)
            .Select(static bundle => bundle.Location!.Value)
            .ToArray();
        if (points.Length == 0)
        {
            return [];
        }

        var center = GeoMath.Centroid(points);
        return
        [
            new GeoCircle
            {
                Center = center,
                RadiusMeters = Math.Max(200, GeoMath.RadiusMeters(center, points) + 50),
            },
        ];
    }
}
