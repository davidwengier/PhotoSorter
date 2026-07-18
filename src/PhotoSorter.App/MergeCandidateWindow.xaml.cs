using PhotoSorter.App.ViewModels;
using PhotoSorter.Infrastructure.Cache;

namespace PhotoSorter.App;

public partial class MergeCandidateWindow : Window
{
    private readonly PreviewWindowPlacementStore _previewWindowPlacementStore;

    public MergeCandidateWindow(
        CandidateViewModel currentCandidate,
        IReadOnlyList<CandidateViewModel> candidates,
        string picturesRoot,
        PreviewWindowPlacementStore previewWindowPlacementStore)
    {
        ArgumentNullException.ThrowIfNull(currentCandidate);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(previewWindowPlacementStore);
        if (candidates.Count == 0)
        {
            throw new ArgumentException(
                "At least one merge candidate is required.",
                nameof(candidates));
        }

        _previewWindowPlacementStore = previewWindowPlacementStore;
        InitializeComponent();
        ViewModel = new MergeCandidatePickerViewModel(
            currentCandidate,
            candidates,
            picturesRoot);
        DataContext = ViewModel;
    }

    public MergeCandidatePickerViewModel ViewModel { get; }

    public CandidateViewModel? CandidateToMerge { get; private set; }

    private void OnMergeClick(object sender, RoutedEventArgs eventArgs)
    {
        CandidateToMerge = ViewModel.SelectedOption?.Candidate;
        if (CandidateToMerge is not null)
        {
            DialogResult = true;
        }
    }

    private void OnPhotoPreviewClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement { DataContext: BundleViewModel bundle }
            || ViewModel.SelectedOption is not { } option)
        {
            return;
        }

        var window = new PhotoPreviewWindow(
            option.Bundles,
            bundle,
            _previewWindowPlacementStore,
            showIncludeToggle: false)
        {
            Owner = this,
        };
        window.ShowDialog();
    }
}
