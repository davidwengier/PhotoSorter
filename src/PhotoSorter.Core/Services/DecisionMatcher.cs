using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Services;

public sealed class DecisionMatcher
{
    public bool IsSuppressedRoutine(
        GeoPoint point,
        IEnumerable<RoutineLocationDecision> decisions) =>
        decisions.Any(
            decision => GeoMath.DistanceMeters(point, decision.Center) <= decision.RadiusMeters);

    public bool IsIgnored(CandidateGroup candidate, IEnumerable<IgnoredGroupRule> rules)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules.Where(rule => rule.Kind == candidate.Kind))
        {
            var paddedStart = rule.Start.AddMinutes(-rule.TimePaddingMinutes);
            var paddedEnd = rule.End.AddMinutes(rule.TimePaddingMinutes);
            if (candidate.End < paddedStart || candidate.Start > paddedEnd)
            {
                continue;
            }

            var points = candidate.Bundles
                .Where(static bundle => bundle.Location is not null)
                .Select(static bundle => bundle.Location!.Value)
                .ToArray();
            if (points.Length == 0)
            {
                continue;
            }

            var matched = points.Count(
                point => rule.Areas.Any(area => GeoMath.Contains(area, point)));
            if ((double)matched / points.Length >= rule.RequiredLocationMatchFraction)
            {
                return true;
            }
        }

        return false;
    }

    public IgnoredGroupRule CreateIgnoredRule(CandidateGroup candidate, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return new IgnoredGroupRule
        {
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            Kind = candidate.Kind,
            Start = candidate.Start,
            End = candidate.End,
            TimePaddingMinutes = candidate.Kind == CandidateKind.Trip ? 360 : 90,
            RequiredLocationMatchFraction = candidate.Kind == CandidateKind.Trip ? 0.3 : 0.5,
            Areas = candidate.Areas.ToList(),
        };
    }
}
