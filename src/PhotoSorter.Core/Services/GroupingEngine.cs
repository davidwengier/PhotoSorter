using System.Globalization;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Services;

public sealed class GroupingEngine(DecisionMatcher decisionMatcher)
{
    private readonly DecisionMatcher _decisionMatcher = decisionMatcher;

    public GroupingResult Analyze(
        IReadOnlyList<AssetBundle> bundles,
        PhotoSorterState state,
        GroupingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bundles);
        ArgumentNullException.ThrowIfNull(state);

        options ??= new GroupingOptions();
        return new GroupingResult
        {
            Candidates = FindCandidates(bundles, state, options),
        };
    }

    private IReadOnlyList<CandidateGroup> FindCandidates(
        IReadOnlyList<AssetBundle> bundles,
        PhotoSorterState state,
        GroupingOptions options)
    {
        var routineDecisions = state.RoutineLocations
            .Where(static decision => decision.Disposition == RoutineLocationDisposition.Routine
                && decision.SuppressCandidates)
            .ToArray();

        var anchors = bundles
            .Where(static bundle => bundle.Location is not null)
            .Where(static bundle => bundle.TimestampConfidence != MetadataConfidence.Low)
            .Where(bundle => !_decisionMatcher.IsSuppressedRoutine(bundle.Location!.Value, routineDecisions))
            .OrderBy(static bundle => bundle.Year)
            .ThenBy(static bundle => bundle.CapturedAt)
            .ToArray();

        var candidates = new List<CandidateGroup>();
        foreach (var yearGroup in anchors.GroupBy(static bundle => bundle.Year))
        {
            var episodes = BuildEpisodes(yearGroup.ToArray(), options)
                .Where(episode => episode.Bundles.Count >= options.EventMinimumAnchors)
                .ToArray();
            candidates.AddRange(BuildYearCandidates(episodes, routineDecisions, options));
        }

        candidates = AttachTimeOnlyBundles(candidates, bundles, options);
        return candidates
            .Where(candidate => !_decisionMatcher.IsIgnored(candidate, state.IgnoredGroups))
            .OrderBy(static candidate => candidate.Start)
            .ThenBy(static candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<Episode> BuildEpisodes(
        IReadOnlyList<AssetBundle> anchors,
        GroupingOptions options)
    {
        var episodes = new List<Episode>();
        Episode? current = null;

        foreach (var bundle in anchors.OrderBy(static bundle => bundle.CapturedAt))
        {
            if (current is null)
            {
                current = new Episode(bundle);
                episodes.Add(current);
                continue;
            }

            var gap = bundle.CapturedAt - current.End;
            var distance = GeoMath.DistanceMeters(current.Center, bundle.Location!.Value);
            if (gap.TotalHours > options.EventMaximumGapHours
                || gap < TimeSpan.Zero
                || distance > options.EventRadiusMeters)
            {
                current = new Episode(bundle);
                episodes.Add(current);
            }
            else
            {
                current.Add(bundle);
            }
        }

        return episodes;
    }

    private static IReadOnlyList<CandidateGroup> BuildYearCandidates(
        IReadOnlyList<Episode> episodes,
        IReadOnlyList<RoutineLocationDecision> routineLocations,
        GroupingOptions options)
    {
        var candidates = new List<CandidateGroup>();
        var chain = new List<Episode>();

        foreach (var episode in episodes.OrderBy(static episode => episode.Start))
        {
            if (chain.Count == 0)
            {
                chain.Add(episode);
                continue;
            }

            if (ShouldMergeIntoTrip(chain, episode, routineLocations, options))
            {
                chain.Add(episode);
            }
            else
            {
                AddChainCandidates(chain, candidates, options);
                chain.Clear();
                chain.Add(episode);
            }
        }

        AddChainCandidates(chain, candidates, options);
        return candidates;
    }

    private static bool ShouldMergeIntoTrip(
        IReadOnlyList<Episode> chain,
        Episode next,
        IReadOnlyList<RoutineLocationDecision> routineLocations,
        GroupingOptions options)
    {
        var last = chain[^1];
        var gapHours = (next.Start - last.End).TotalHours;
        if (gapHours is < 0 || gapHours > options.TripMaximumGapHours)
        {
            return false;
        }

        var movement = GeoMath.DistanceMeters(last.Center, next.Center);
        if (movement >= options.TripMinimumMovementMeters)
        {
            return true;
        }

        var combinedDuration = next.End - chain[0].Start;
        if (combinedDuration.TotalHours < options.TripMinimumDurationHours)
        {
            return false;
        }

        return routineLocations.Count > 0
            && chain.Append(next).All(
                episode => routineLocations.All(
                    routine => GeoMath.DistanceMeters(episode.Center, routine.Center)
                        >= options.TripRoutineDistanceMeters));
    }

    private static void AddChainCandidates(
        IReadOnlyList<Episode> chain,
        ICollection<CandidateGroup> candidates,
        GroupingOptions options)
    {
        if (chain.Count == 0)
        {
            return;
        }

        var duration = chain[^1].End - chain[0].Start;
        if (chain.Count >= 2 && duration.TotalHours >= options.TripMinimumDurationHours)
        {
            candidates.Add(CreateTripCandidate(chain));
            return;
        }

        foreach (var episode in chain)
        {
            candidates.Add(CreateEventCandidate(episode));
        }
    }

    private static CandidateGroup CreateEventCandidate(Episode episode)
    {
        var area = CreateArea(episode.Bundles);
        var score = Math.Min(
            100,
            42
            + (episode.Bundles.Count * 3)
            + Math.Max(0, 20 - (area.RadiusMeters / 200)));

        return new CandidateGroup
        {
            Id = StableId.Create("event", episode.Bundles.Select(static bundle => bundle.Id)),
            Kind = CandidateKind.Event,
            Year = episode.Bundles[0].Year,
            Start = episode.Start,
            End = episode.End,
            Bundles = episode.Bundles.ToArray(),
            Areas = [area],
            Score = score,
            Reasons =
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{episode.Bundles.Count} geotagged items within {area.RadiusMeters / 1000:0.0} km over {(episode.End - episode.Start).TotalHours:0.#} hours."),
            ],
        };
    }

    private static CandidateGroup CreateTripCandidate(IReadOnlyList<Episode> episodes)
    {
        var bundles = episodes.SelectMany(static episode => episode.Bundles).ToArray();
        var areas = episodes.Select(episode => CreateArea(episode.Bundles)).ToArray();
        var distance = episodes
            .Zip(episodes.Skip(1), static (first, second) => GeoMath.DistanceMeters(first.Center, second.Center))
            .Sum();
        var score = Math.Min(
            100,
            55 + (episodes.Count * 6) + Math.Min(20, distance / 10_000));

        return new CandidateGroup
        {
            Id = StableId.Create("trip", bundles.Select(static bundle => bundle.Id)),
            Kind = CandidateKind.Trip,
            Year = bundles[0].Year,
            Start = bundles.Min(static bundle => bundle.CapturedAt),
            End = bundles.Max(static bundle => bundle.CapturedAt),
            Bundles = bundles,
            Areas = areas,
            Score = score,
            Reasons =
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{episodes.Count} location segments over {(bundles.Max(static bundle => bundle.CapturedAt) - bundles.Min(static bundle => bundle.CapturedAt)).TotalDays:0.#} days."),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Approximately {distance / 1000:0.#} km between segment centres."),
            ],
        };
    }

    private static List<CandidateGroup> AttachTimeOnlyBundles(
        IReadOnlyList<CandidateGroup> candidates,
        IReadOnlyList<AssetBundle> allBundles,
        GroupingOptions options)
    {
        var assigned = candidates
            .SelectMany(static candidate => candidate.Bundles)
            .Select(static bundle => bundle.Id)
            .ToHashSet(StringComparer.Ordinal);
        var additions = candidates.ToDictionary(
            static candidate => candidate.Id,
            static _ => new List<AssetBundle>(),
            StringComparer.Ordinal);

        foreach (var bundle in allBundles
                     .Where(static bundle => bundle.Location is null)
                     .Where(static bundle => bundle.TimestampConfidence >= MetadataConfidence.Medium)
                     .Where(bundle => !assigned.Contains(bundle.Id)))
        {
            var matching = candidates
                .Where(candidate => candidate.Year == bundle.Year)
                .Where(candidate =>
                {
                    var padding = TimeSpan.FromHours(
                        candidate.Kind == CandidateKind.Trip
                            ? options.AttachmentPaddingHours * 2
                            : options.AttachmentPaddingHours);
                    return bundle.CapturedAt >= candidate.Start - padding
                        && bundle.CapturedAt <= candidate.End + padding;
                })
                .OrderBy(candidate => TemporalDistance(bundle.CapturedAt, candidate))
                .ToArray();

            if (matching.Length == 1
                || (matching.Length > 1
                    && TemporalDistance(bundle.CapturedAt, matching[0])
                    < TemporalDistance(bundle.CapturedAt, matching[1]) / 2))
            {
                additions[matching[0].Id].Add(bundle);
            }
        }

        return candidates
            .Select(candidate =>
            {
                if (additions[candidate.Id].Count == 0)
                {
                    return candidate;
                }

                var combined = candidate.Bundles
                    .Concat(additions[candidate.Id])
                    .OrderBy(static bundle => bundle.CapturedAt)
                    .ToArray();
                return candidate with
                {
                    Id = StableId.Create(
                        candidate.Kind == CandidateKind.Trip ? "trip" : "event",
                        combined.Select(static bundle => bundle.Id)),
                    Start = combined.Min(static bundle => bundle.CapturedAt),
                    End = combined.Max(static bundle => bundle.CapturedAt),
                    Bundles = combined,
                    Reasons = candidate.Reasons
                        .Append($"{additions[candidate.Id].Count} time-adjacent items without GPS were attached.")
                        .ToArray(),
                };
            })
            .ToList();
    }

    private static double TemporalDistance(DateTimeOffset timestamp, CandidateGroup candidate)
    {
        if (timestamp < candidate.Start)
        {
            return (candidate.Start - timestamp).TotalSeconds;
        }

        return timestamp > candidate.End
            ? (timestamp - candidate.End).TotalSeconds
            : 0;
    }

    private static GeoCircle CreateArea(IReadOnlyList<AssetBundle> bundles)
    {
        var points = bundles
            .Where(static bundle => bundle.Location is not null)
            .Select(static bundle => bundle.Location!.Value)
            .ToArray();
        var center = GeoMath.Centroid(points);
        return new GeoCircle
        {
            Center = center,
            RadiusMeters = Math.Max(200, GeoMath.RadiusMeters(center, points) + 50),
        };
    }

    private sealed class Episode
    {
        private double _latitudeSum;
        private double _longitudeSum;

        public Episode(AssetBundle first)
        {
            Bundles.Add(first);
            _latitudeSum = first.Location!.Value.Latitude;
            _longitudeSum = first.Location.Value.Longitude;
            Start = first.CapturedAt;
            End = first.CapturedAt;
        }

        public List<AssetBundle> Bundles { get; } = [];

        public DateTimeOffset Start { get; private set; }

        public DateTimeOffset End { get; private set; }

        public GeoPoint Center => new(
            _latitudeSum / Bundles.Count,
            _longitudeSum / Bundles.Count);

        public void Add(AssetBundle bundle)
        {
            Bundles.Add(bundle);
            _latitudeSum += bundle.Location!.Value.Latitude;
            _longitudeSum += bundle.Location.Value.Longitude;
            Start = Start < bundle.CapturedAt ? Start : bundle.CapturedAt;
            End = End > bundle.CapturedAt ? End : bundle.CapturedAt;
        }
    }
}
