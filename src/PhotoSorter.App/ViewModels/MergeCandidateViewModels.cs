using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoSorter.App.ViewModels;

public sealed class MergeCandidateOptionViewModel
{
    public MergeCandidateOptionViewModel(
        CandidateViewModel currentCandidate,
        CandidateViewModel candidate,
        string picturesRoot)
    {
        Candidate = candidate;
        Bundles = candidate.Model.Bundles
            .OrderBy(static bundle => bundle.CapturedAt)
            .Select(bundle => new BundleViewModel(picturesRoot, bundle))
            .ToArray();
        PreviewBundle = Bundles[0];
        GapText = CreateGapText(currentCandidate, candidate);
    }

    public CandidateViewModel Candidate { get; }

    public IReadOnlyList<BundleViewModel> Bundles { get; }

    public BundleViewModel PreviewBundle { get; }

    public string GapText { get; }

    private static string CreateGapText(
        CandidateViewModel currentCandidate,
        CandidateViewModel candidate)
    {
        if (candidate.Model.End < currentCandidate.Model.Start)
        {
            return $"{FormatGap(currentCandidate.Model.Start - candidate.Model.End)} earlier";
        }

        if (candidate.Model.Start > currentCandidate.Model.End)
        {
            return $"{FormatGap(candidate.Model.Start - currentCandidate.Model.End)} later";
        }

        return "Overlaps this group";
    }

    private static string FormatGap(TimeSpan gap)
    {
        if (gap.TotalMinutes < 90)
        {
            return $"{Math.Max(1, Math.Round(gap.TotalMinutes)):0} minutes";
        }

        if (gap.TotalHours < 36)
        {
            return $"{Math.Round(gap.TotalHours):0} hours";
        }

        if (gap.TotalDays < 60)
        {
            return $"{Math.Round(gap.TotalDays):0} days";
        }

        if (gap.TotalDays < 548)
        {
            return $"{Math.Round(gap.TotalDays / 30.4375):0} months";
        }

        return $"{Math.Round(gap.TotalDays / 365.25, 1):0.#} years";
    }
}

public sealed partial class MergeCandidatePickerViewModel : ObservableObject
{
    public MergeCandidatePickerViewModel(
        CandidateViewModel currentCandidate,
        IReadOnlyList<CandidateViewModel> candidates,
        string picturesRoot)
    {
        CurrentCandidate = currentCandidate;
        Options = candidates
            .Select(candidate => new MergeCandidateOptionViewModel(
                currentCandidate,
                candidate,
                picturesRoot))
            .ToArray();
        SelectedOption = Options.Count > 0 ? Options[0] : null;
    }

    public CandidateViewModel CurrentCandidate { get; }

    public IReadOnlyList<MergeCandidateOptionViewModel> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private MergeCandidateOptionViewModel? _selectedOption;

    public string ResultSummary
    {
        get
        {
            if (SelectedOption is null)
            {
                return string.Empty;
            }

            var bundles = CurrentCandidate.Model.Bundles
                .Concat(SelectedOption.Candidate.Model.Bundles)
                .DistinctBy(static bundle => bundle.Id)
                .ToArray();
            var start = bundles.Min(static bundle => bundle.CapturedAt);
            var end = bundles.Max(static bundle => bundle.CapturedAt);
            var period = start.Date == end.Date
                ? start.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture)
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"{start:ddd d MMM yyyy} to {end:ddd d MMM yyyy}");
            return $"Result: {bundles.Length:N0} items spanning {period}.";
        }
    }
}
