using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PhotoSorter.App.Services;
using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;
using PhotoSorter.Infrastructure.State;

namespace PhotoSorter.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const string AllYears = "All years";

    private readonly ISharedStateStore _stateStore;
    private readonly IMediaScanner _mediaScanner;
    private readonly GroupingEngine _groupingEngine;
    private readonly DecisionMatcher _decisionMatcher;
    private readonly CandidateEditor _candidateEditor;
    private readonly MovePlanner _movePlanner;
    private readonly IMoveExecutor _moveExecutor;
    private readonly IReverseGeocoder _reverseGeocoder;
    private readonly IRecentRootStore _recentRootStore;
    private readonly IUserDialogService _dialogs;
    private readonly IAppUpdateService _appUpdateService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dictionary<string, HashSet<string>> _excludedBundles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _destinationDrafts = new(StringComparer.Ordinal);
    private readonly List<CandidateGroup> _candidateModels = [];
    private readonly HashSet<string> _pendingPlaceNames = new(StringComparer.Ordinal);

    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _namingCancellation;
    private readonly CancellationTokenSource _updateCancellation = new();
    private MediaScanResult? _scanResult;
    private PhotoSorterState? _state;

    [ObservableProperty]
    private string _picturesRoot = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Choose a Pictures folder to begin.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private CandidateViewModel? _selectedCandidate;

    [ObservableProperty]
    private BundleViewModel? _selectedBundle;

    [ObservableProperty]
    private string _selectedYearFilter = AllYears;

    [ObservableProperty]
    private string _destinationFolderName = string.Empty;

    [ObservableProperty]
    private string _destinationPrefix = string.Empty;

    [ObservableProperty]
    private string _sourceFolderSummary = string.Empty;

    [ObservableProperty]
    private string _sourceFolderDetails = string.Empty;

    [ObservableProperty]
    private bool _isPhotoGridView;

    [ObservableProperty]
    private bool _hasCandidates;

    [ObservableProperty]
    private string _emptyStateMessage = "Choose a Pictures folder to find photo groups.";

    [ObservableProperty]
    private bool _isUpdateReady;

    [ObservableProperty]
    private string _updateMessage = string.Empty;

    public MainViewModel(
        ISharedStateStore stateStore,
        IMediaScanner mediaScanner,
        GroupingEngine groupingEngine,
        DecisionMatcher decisionMatcher,
        CandidateEditor candidateEditor,
        MovePlanner movePlanner,
        IMoveExecutor moveExecutor,
        IReverseGeocoder reverseGeocoder,
        IRecentRootStore recentRootStore,
        IUserDialogService dialogs,
        IAppUpdateService appUpdateService,
        ILogger<MainViewModel> logger)
    {
        _stateStore = stateStore;
        _mediaScanner = mediaScanner;
        _groupingEngine = groupingEngine;
        _decisionMatcher = decisionMatcher;
        _candidateEditor = candidateEditor;
        _movePlanner = movePlanner;
        _moveExecutor = moveExecutor;
        _reverseGeocoder = reverseGeocoder;
        _recentRootStore = recentRootStore;
        _dialogs = dialogs;
        _appUpdateService = appUpdateService;
        _logger = logger;

        YearFilters.Add(AllYears);
    }

    public ObservableCollection<CandidateViewModel> Candidates { get; } = [];

    public ObservableCollection<BundleViewModel> SelectedBundles { get; } = [];

    public ObservableCollection<string> YearFilters { get; } = [];

    public ObservableCollection<string> ExistingFolders { get; } = [];

    public string Attribution => _reverseGeocoder.Attribution;

    public bool IsPhotoListView
    {
        get => !IsPhotoGridView;
        set
        {
            if (value)
            {
                IsPhotoGridView = false;
            }
        }
    }

    public void Dispose()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _namingCancellation?.Cancel();
        _namingCancellation?.Dispose();
        _updateCancellation.Cancel();
        _updateCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await _appUpdateService.DownloadLatestAsync(_updateCancellation.Token);
            if (update is null)
            {
                return;
            }

            UpdateMessage =
                $"PhotoSorter {update.Version} is ready. Restart when convenient to finish updating.";
            IsUpdateReady = true;
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested)
        {
            _logger.LogDebug("Update check was cancelled while PhotoSorter was closing.");
        }
    }

    public async Task InitializeAsync()
    {
        var recentRoot = await _recentRootStore.LoadAsync();
        if (!string.IsNullOrWhiteSpace(recentRoot))
        {
            PicturesRoot = recentRoot;
            await ScanCurrentRootAsync();
            return;
        }

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (Directory.Exists(pictures))
        {
            PicturesRoot = pictures;
        }
    }

    [RelayCommand]
    private async Task BrowseRootAsync()
    {
        var selected = _dialogs.ChooseFolder(
            "Choose the Pictures folder that contains the year folders",
            PicturesRoot);
        if (selected is null)
        {
            return;
        }

        PicturesRoot = selected;
        await ScanCurrentRootAsync();
    }

    [RelayCommand]
    private async Task ScanLibraryAsync() =>
        await ScanCurrentRootAsync();

    [RelayCommand]
    private void CancelScan() => _scanCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
    private void RestartToUpdate()
    {
        if (!_appUpdateService.ApplyAndRestart())
        {
            _dialogs.ShowError(
                "PhotoSorter update",
                "The downloaded update could not be started. Please close and reopen PhotoSorter to try again.");
        }
    }

    private bool CanRestartToUpdate() => IsUpdateReady && !IsBusy;

    [RelayCommand]
    private async Task IgnoreCandidateAsync()
    {
        if (SelectedCandidate is null || _state is null)
        {
            return;
        }

        var rule = _decisionMatcher.CreateIgnoredRule(
            SelectedCandidate.Model,
            SelectedCandidate.Title);
        var message =
            $"Ignore '{SelectedCandidate.Title}'? PhotoSorter will remember this date and place "
            + "so this group is not suggested again.";
        if (!_dialogs.Confirm("Ignore this suggestion", message))
        {
            return;
        }

        if (!await PersistStateAsync(state => state with
        {
            IgnoredGroups = [.. state.IgnoredGroups, rule],
        }))
        {
            return;
        }

        await RegroupAsync();
    }

    [RelayCommand]
    private async Task IgnoreLocationAsync()
    {
        if (SelectedCandidate is null || _state is null || SelectedCandidate.Model.Areas.Count == 0)
        {
            return;
        }

        await EnsureCandidateNamedAsync(SelectedCandidate);
        var placeName = SelectedCandidate.PrimaryPlaceLabel
            ?? SelectedCandidate.PlaceLabel
            ?? "this place";
        if (!_dialogs.Confirm(
                "Always ignore this place",
                $"Always ignore photo groups around '{placeName}'? "
                + "This decision will apply on every machine using this Pictures folder."))
        {
            return;
        }

        var area = SelectedCandidate.Model.Areas[0];
        var decision = new RoutineLocationDecision
        {
            Name = placeName,
            Disposition = RoutineLocationDisposition.Routine,
            Center = area.Center,
            RadiusMeters = Math.Clamp(area.RadiusMeters, 250, 1_500),
            SuppressCandidates = true,
        };
        if (!await PersistStateAsync(state => state with
        {
            RoutineLocations = [.. state.RoutineLocations, decision],
        }))
        {
            return;
        }

        await RegroupAsync();
    }

    [RelayCommand]
    private void SplitCandidate()
    {
        if (SelectedCandidate is null || SelectedBundle is null)
        {
            return;
        }

        try
        {
            var split = _candidateEditor.Split(
                SelectedCandidate.Model,
                SelectedBundle.Model.CapturedAt);
            var index = _candidateModels.FindIndex(candidate => candidate.Id == SelectedCandidate.Model.Id);
            if (index < 0)
            {
                return;
            }

            _candidateModels.RemoveAt(index);
            _candidateModels.Insert(index, split.After);
            _candidateModels.Insert(index, split.Before);
            ApplyCandidateFilters(split.After.Id);
        }
        catch (ArgumentException exception)
        {
            _dialogs.ShowError("Split candidate", exception.Message);
        }
    }

    [RelayCommand]
    private void MergeNextCandidate()
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        var ordered = _candidateModels.OrderBy(static candidate => candidate.Start).ToArray();
        var index = Array.FindIndex(ordered, candidate => candidate.Id == SelectedCandidate.Model.Id);
        var next = ordered.Skip(index + 1).FirstOrDefault(
            candidate => candidate.Year == SelectedCandidate.Model.Year);
        if (index < 0 || next is null)
        {
            _dialogs.ShowInformation("Merge candidates", "There is no later candidate in the same year.");
            return;
        }

        if (!_dialogs.Confirm(
                "Merge candidates",
                $"Merge '{SelectedCandidate.Title}' with the candidate beginning {next.Start:g}?"))
        {
            return;
        }

        var merged = _candidateEditor.Merge(SelectedCandidate.Model, next);
        _candidateModels.RemoveAll(
            candidate => candidate.Id == SelectedCandidate.Model.Id || candidate.Id == next.Id);
        _candidateModels.Add(merged);
        ApplyCandidateFilters(merged.Id);
    }

    [RelayCommand]
    private void ExpandCandidate()
    {
        if (SelectedCandidate is null || _scanResult is null)
        {
            return;
        }

        var assigned = _candidateModels
            .SelectMany(static candidate => candidate.Bundles)
            .Select(static bundle => bundle.Id)
            .ToHashSet(StringComparer.Ordinal);
        var padding = TimeSpan.FromHours(2);
        var nearby = _scanResult.Bundles
            .Where(bundle => bundle.Year == SelectedCandidate.Model.Year)
            .Where(bundle => !assigned.Contains(bundle.Id))
            .Where(static bundle => bundle.TimestampConfidence >= MetadataConfidence.Medium)
            .Where(bundle => bundle.CapturedAt >= SelectedCandidate.Model.Start - padding
                && bundle.CapturedAt <= SelectedCandidate.Model.End + padding)
            .ToArray();
        if (nearby.Length == 0)
        {
            _dialogs.ShowInformation("Expand candidate", "No unassigned time-adjacent items were found.");
            return;
        }

        var expanded = _candidateEditor.AddBundles(SelectedCandidate.Model, nearby);
        ReplaceCandidate(SelectedCandidate.Model.Id, expanded);
        ApplyCandidateFilters(expanded.Id);
    }

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        if (SelectedCandidate is null
            || string.IsNullOrWhiteSpace(PicturesRoot)
            || IsBusy)
        {
            return;
        }

        var candidate = SelectedCandidate;
        await EnsureCandidateNamedAsync(candidate);
        if (SelectedCandidate?.Model.Id != candidate.Model.Id)
        {
            return;
        }

        SaveCurrentDraft();
        var selected = SelectedBundles
            .Where(static bundle => bundle.IsIncluded)
            .Select(static bundle => bundle.Model)
            .ToArray();
        var build = _movePlanner.Build(
            PicturesRoot,
            candidate.Model,
            selected,
            DestinationFolderName);
        if (!build.IsValid)
        {
            _dialogs.ShowError("Move plan", string.Join(Environment.NewLine, build.Errors));
            return;
        }

        var plan = build.Plan!;
        var preflight = await _moveExecutor.PreflightAsync(plan);
        if (!preflight.IsValid)
        {
            _dialogs.ShowError("Move preflight", string.Join(Environment.NewLine, preflight.Errors));
            return;
        }

        if (!_dialogs.Confirm(
                "Name and move these photos",
                $"Move {plan.Entries.Count:N0} files to "
                + $"'{plan.DestinationDirectoryRelativePath}'? "
                + "Existing files will never be overwritten. There is no undo or automatic rollback."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Moving {candidate.Title}…";
            var result = await _moveExecutor.ExecuteAsync(
                plan,
                new Progress<int>(value => ProgressPercent = value));

            if (!result.Succeeded)
            {
                _dialogs.ShowError("Move stopped", BuildMoveFailureMessage(result));
            }
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 0;
        }

        await ScanCurrentRootAsync();
    }

    partial void OnSelectedCandidateChanging(CandidateViewModel? value) => SaveCurrentDraft();

    partial void OnSelectedCandidateChanged(CandidateViewModel? value)
    {
        foreach (var bundle in SelectedBundles)
        {
            bundle.PropertyChanged -= OnBundlePropertyChanged;
        }

        SelectedBundles.Clear();
        ExistingFolders.Clear();
        SelectedBundle = null;

        if (value is null)
        {
            DestinationFolderName = string.Empty;
            DestinationPrefix = string.Empty;
            SourceFolderSummary = string.Empty;
            SourceFolderDetails = string.Empty;
            return;
        }

        RefreshSourceFolderDisplay(value.Model);
        _excludedBundles.TryGetValue(value.Model.Id, out var excluded);
        foreach (var bundle in value.Model.Bundles.OrderBy(static bundle => bundle.CapturedAt))
        {
            var viewModel = new BundleViewModel(PicturesRoot, bundle)
            {
                IsIncluded = excluded?.Contains(bundle.Id) is not true,
            };
            viewModel.PropertyChanged += OnBundlePropertyChanged;
            SelectedBundles.Add(viewModel);
        }

        SelectedBundle = SelectedBundles.FirstOrDefault();
        RefreshExistingFolders(value.Model.Year);
        DestinationPrefix = Path.Combine(
                PicturesRoot,
                value.Model.Year.ToString(CultureInfo.InvariantCulture))
            + Path.DirectorySeparatorChar;
        DestinationFolderName = _destinationDrafts.GetValueOrDefault(value.Model.Id)
            ?? CreateSuggestedFolderName(value.Model);
    }

    partial void OnSelectedYearFilterChanged(string value) => ApplyCandidateFilters();

    partial void OnDestinationFolderNameChanged(string value) => SaveCurrentDraft();

    partial void OnIsBusyChanged(bool value) => RestartToUpdateCommand.NotifyCanExecuteChanged();

    partial void OnIsUpdateReadyChanged(bool value) => RestartToUpdateCommand.NotifyCanExecuteChanged();

    partial void OnIsPhotoGridViewChanged(bool value) => OnPropertyChanged(nameof(IsPhotoListView));

    private async Task ScanCurrentRootAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(PicturesRoot))
        {
            return;
        }

        var fullRoot = Path.GetFullPath(PicturesRoot);
        if (!Directory.Exists(fullRoot))
        {
            _dialogs.ShowError("Pictures folder", $"'{fullRoot}' does not exist.");
            return;
        }

        CancelBackgroundNaming();
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        try
        {
            PicturesRoot = fullRoot;
            _state = await _stateStore.LoadAsync(fullRoot, _scanCancellation.Token);
            await _recentRootStore.SaveAsync(fullRoot, _scanCancellation.Token);

            var progress = new Progress<ScanProgress>(scanProgress =>
            {
                ProgressPercent = scanProgress.Total == 0
                    ? 0
                    : scanProgress.Processed * 100d / scanProgress.Total;
                StatusMessage = scanProgress.Phase switch
                {
                    ScanPhase.Discovering => "Discovering Phone Images folders…",
                    ScanPhase.ReadingMetadata =>
                        $"Reading metadata {scanProgress.Processed:N0}/{scanProgress.Total:N0}…",
                    ScanPhase.SavingCache => "Updating the disposable local cache…",
                    ScanPhase.Grouping => "Finding photo groups…",
                    ScanPhase.Completed => "Scan complete.",
                    _ => scanProgress.Phase.ToString(),
                };
            });
            _scanResult = await Task.Run(
                () => _mediaScanner.ScanAsync(
                    fullRoot,
                    _state,
                    progress,
                    _scanCancellation.Token),
                _scanCancellation.Token);

            var grouping = await Task.Run(
                () => _groupingEngine.Analyze(_scanResult.Bundles, _state),
                _scanCancellation.Token);
            SetGrouping(grouping);

            StatusMessage =
                $"Indexed {_scanResult.Assets.Count:N0} files: "
                + $"{_scanResult.ReusedMetadataCount:N0} reused, "
                + $"{_scanResult.ExtractedMetadataCount:N0} read, "
                + $"{Candidates.Count:N0} suggestions.";
            _logger.LogInformation(
                "Scan completed with {AssetCount} assets, {CandidateCount} candidates and {IssueCount} issues.",
                _scanResult.Assets.Count,
                grouping.Candidates.Count,
                _scanResult.Issues.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled. No decisions were changed.";
        }
        catch (Exception exception) when (IsExpectedUserFacingException(exception))
        {
            _dialogs.ShowError("PhotoSorter could not scan this folder", exception.Message);
            _logger.LogError(exception, "Library scan failed.");
            StatusMessage = "Scan failed.";
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 0;
        }
    }

    private async Task RegroupAsync()
    {
        if (_scanResult is null || _state is null)
        {
            return;
        }

        var grouping = await Task.Run(() => _groupingEngine.Analyze(_scanResult.Bundles, _state));
        SetGrouping(grouping);
        StatusMessage = $"Updated suggestions: {Candidates.Count:N0} groups.";
    }

    private void SetGrouping(GroupingResult grouping)
    {
        _candidateModels.Clear();
        _candidateModels.AddRange(grouping.Candidates);
        _excludedBundles.Clear();
        _destinationDrafts.Clear();

        RefreshYearFilters();
        ApplyCandidateFilters();
        StartBackgroundNaming();
    }

    private void ApplyCandidateFilters(string? preferredCandidateId = null)
    {
        preferredCandidateId ??= SelectedCandidate?.Model.Id;
        var selectedYear = int.TryParse(
            SelectedYearFilter,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var year)
            ? year
            : (int?)null;

        Candidates.Clear();
        foreach (var candidate in _candidateModels
                     .Where(candidate => selectedYear is null || candidate.Year == selectedYear)
                     .OrderByDescending(static candidate => candidate.Score)
                     .ThenByDescending(static candidate => candidate.Start))
        {
            Candidates.Add(new CandidateViewModel(candidate)
            {
                IsNaming = _pendingPlaceNames.Contains(candidate.Id),
            });
        }

        HasCandidates = Candidates.Count > 0;
        EmptyStateMessage = _scanResult is null
            ? "Choose a Pictures folder to find photo groups."
            : "No photo groups need your attention.";
        SelectedCandidate = Candidates.FirstOrDefault(
                candidate => candidate.Model.Id == preferredCandidateId)
            ?? Candidates.FirstOrDefault();
    }

    private void RefreshYearFilters()
    {
        var previous = SelectedYearFilter;
        YearFilters.Clear();
        YearFilters.Add(AllYears);
        foreach (var year in _candidateModels.Select(static candidate => candidate.Year).Distinct().OrderDescending())
        {
            YearFilters.Add(year.ToString(CultureInfo.InvariantCulture));
        }

        SelectedYearFilter = YearFilters.Contains(previous) ? previous : AllYears;
    }

    private void RefreshExistingFolders(int year)
    {
        var yearPath = Path.Combine(PicturesRoot, year.ToString(CultureInfo.InvariantCulture));
        if (!Directory.Exists(yearPath))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(yearPath)
                     .Select(Path.GetFileName)
                     .Where(static name => !string.IsNullOrWhiteSpace(name))
                     .Where(static name => !string.Equals(name, "Phone Images", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.CurrentCultureIgnoreCase))
        {
            ExistingFolders.Add(folder!);
        }
    }

    private void RefreshSourceFolderDisplay(CandidateGroup candidate)
    {
        var relativeFolders = candidate.Bundles
            .SelectMany(static bundle => bundle.Assets)
            .Select(static asset => Path.GetDirectoryName(asset.RelativePath))
            .Where(static folder => !string.IsNullOrWhiteSpace(folder))
            .Select(static folder => folder!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SourceFolderSummary = relativeFolders.Length switch
        {
            0 => "Unknown",
            1 => relativeFolders[0],
            _ => $"{relativeFolders[0]} (+{relativeFolders.Length - 1:N0} more)",
        };
        SourceFolderDetails = string.Join(
            Environment.NewLine,
            relativeFolders.Select(folder => Path.Combine(PicturesRoot, folder)));
    }

    private async Task<bool> PersistStateAsync(Func<PhotoSorterState, PhotoSorterState> update)
    {
        try
        {
            _state = await _stateStore.UpdateAsync(PicturesRoot, update);
            return true;
        }
        catch (Exception exception) when (IsExpectedUserFacingException(exception))
        {
            _dialogs.ShowError("Could not save .photosorter.json", exception.Message);
            _logger.LogError(exception, "Shared state update failed.");
            return false;
        }
    }

    private void StartBackgroundNaming()
    {
        CancelBackgroundNaming();

        var candidateIds = _candidateModels
            .Where(static candidate => candidate.Areas.Count > 0)
            .Where(static candidate => string.IsNullOrWhiteSpace(candidate.PlaceLabel))
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.Start)
            .Select(static candidate => candidate.Id)
            .ToArray();
        _pendingPlaceNames.UnionWith(candidateIds);
        foreach (var candidate in Candidates)
        {
            candidate.IsNaming = _pendingPlaceNames.Contains(candidate.Model.Id);
        }

        if (candidateIds.Length == 0)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _namingCancellation = cancellation;
        _ = NameAllLocationsAsync(candidateIds, cancellation);
    }

    private async Task NameAllLocationsAsync(
        IReadOnlyList<string> candidateIds,
        CancellationTokenSource cancellation)
    {
        try
        {
            foreach (var candidateId in candidateIds)
            {
                await NameCandidateAsync(candidateId, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedUserFacingException(exception))
        {
            StatusMessage = "Place names are temporarily unavailable. Photo review still works.";
            _logger.LogWarning(exception, "Automatic place naming stopped.");
        }
        finally
        {
            if (ReferenceEquals(_namingCancellation, cancellation))
            {
                foreach (var candidate in Candidates)
                {
                    candidate.IsNaming = false;
                }

                _pendingPlaceNames.Clear();
                _namingCancellation.Dispose();
                _namingCancellation = null;
            }
        }
    }

    private async Task NameCandidateAsync(string candidateId, CancellationToken cancellationToken)
    {
        var candidate = _candidateModels.FirstOrDefault(model => model.Id == candidateId);
        if (candidate is null
            || candidate.Areas.Count == 0
            || !string.IsNullOrWhiteSpace(candidate.PlaceLabel))
        {
            MarkCandidateNamingComplete(candidateId);
            return;
        }

        var visibleCandidate = Candidates.FirstOrDefault(item => item.Model.Id == candidateId);
        if (visibleCandidate is not null)
        {
            visibleCandidate.IsNaming = true;
        }

        var previousFolderSuggestion = CreateSuggestedFolderName(candidate);
        try
        {
            var firstArea = candidate.Areas[0];
            var first = await _reverseGeocoder.ReverseGeocodeAsync(
                PicturesRoot,
                firstArea,
                cancellationToken);
            if (first is null)
            {
                return;
            }

            var shortLabel = first.ShortName;
            var fullLabel = first.DisplayName;
            var lastArea = candidate.Areas[^1];
            if (candidate.Areas.Count > 1
                && GeoMath.DistanceMeters(firstArea.Center, lastArea.Center) >= 5_000)
            {
                var last = await _reverseGeocoder.ReverseGeocodeAsync(
                    PicturesRoot,
                    lastArea,
                    cancellationToken);
                if (last is not null
                    && !string.Equals(first.ShortName, last.ShortName, StringComparison.OrdinalIgnoreCase))
                {
                    shortLabel = $"{first.ShortName} to {last.ShortName}";
                    fullLabel = $"{first.DisplayName} to {last.DisplayName}";
                }
            }

            var updated = candidate with
            {
                PlaceLabel = shortLabel,
                FullPlaceLabel = fullLabel,
                PrimaryPlaceLabel = first.ShortName,
            };
            ReplaceCandidate(candidateId, updated);
            visibleCandidate = Candidates.FirstOrDefault(item => item.Model.Id == candidateId);
            visibleCandidate?.ApplyPlace(shortLabel, fullLabel, first.ShortName);
            if (SelectedCandidate?.Model.Id == candidateId
                && string.Equals(
                    DestinationFolderName,
                    previousFolderSuggestion,
                    StringComparison.Ordinal))
            {
                DestinationFolderName = CreateSuggestedFolderName(updated);
            }
        }
        finally
        {
            MarkCandidateNamingComplete(candidateId);
        }
    }

    private async Task EnsureCandidateNamedAsync(CandidateViewModel candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.PlaceLabel)
            || candidate.Model.Areas.Count == 0)
        {
            return;
        }

        try
        {
            await NameCandidateAsync(candidate.Model.Id, CancellationToken.None);
        }
        catch (Exception exception) when (IsExpectedUserFacingException(exception))
        {
            StatusMessage = "This place could not be named automatically. You can still enter a folder name.";
            _logger.LogWarning(exception, "Could not name candidate {CandidateId}.", candidate.Model.Id);
        }
    }

    private void MarkCandidateNamingComplete(string candidateId)
    {
        _pendingPlaceNames.Remove(candidateId);
        var visibleCandidate = Candidates.FirstOrDefault(item => item.Model.Id == candidateId);
        if (visibleCandidate is not null)
        {
            visibleCandidate.IsNaming = false;
        }
    }

    private void CancelBackgroundNaming()
    {
        var cancellation = _namingCancellation;
        _namingCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        _pendingPlaceNames.Clear();
        foreach (var candidate in Candidates)
        {
            candidate.IsNaming = false;
        }
    }

    private static string BuildMoveFailureMessage(MoveExecutionResult result)
    {
        var lines = new List<string>
        {
            "The move stopped at the first failure. Files already moved remain moved.",
        };
        var moved = result.Items.Where(static item => item.Status == MoveItemStatus.Moved).ToArray();
        var failed = result.Items.FirstOrDefault(static item => item.Status == MoveItemStatus.Failed);
        var notAttempted = result.Items
            .Where(static item => item.Status == MoveItemStatus.NotAttempted)
            .ToArray();

        if (moved.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"Moved ({moved.Length:N0}):");
            lines.AddRange(moved.Select(static item =>
                $"{item.Entry.SourceRelativePath} -> {item.Entry.DestinationRelativePath}"));
        }

        if (failed is not null)
        {
            lines.Add(string.Empty);
            lines.Add("Failed:");
            lines.Add(failed.Entry.SourceRelativePath);
            lines.Add(failed.Error ?? "The file could not be moved.");
        }

        if (notAttempted.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"Not attempted ({notAttempted.Length:N0}):");
            lines.AddRange(notAttempted.Select(static item => item.Entry.SourceRelativePath));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateSuggestedFolderName(CandidateGroup candidate)
    {
        var rawName = string.IsNullOrWhiteSpace(candidate.PlaceLabel)
            ? $"Photos {candidate.Start:yyyy-MM-dd}"
            : candidate.PlaceLabel;
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(
                rawName.Select(character => invalidCharacters.Contains(character) ? ' ' : character).ToArray())
            .Trim()
            .TrimEnd('.');
        sanitized = string.Join(
            " ",
            sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (sanitized.Length > 100)
        {
            sanitized = sanitized[..100].TrimEnd();
        }

        return FolderNameValidator.Validate(sanitized).Count == 0
            ? sanitized
            : $"Photos {candidate.Start:yyyy-MM-dd}";
    }

    private void ReplaceCandidate(string oldId, CandidateGroup replacement)
    {
        var index = _candidateModels.FindIndex(candidate => candidate.Id == oldId);
        if (index >= 0)
        {
            _candidateModels[index] = replacement;
        }
    }

    private void SaveCurrentDraft()
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        _destinationDrafts[SelectedCandidate.Model.Id] = DestinationFolderName;
        _excludedBundles[SelectedCandidate.Model.Id] = SelectedBundles
            .Where(static bundle => !bundle.IsIncluded)
            .Select(static bundle => bundle.Model.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void OnBundlePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(BundleViewModel.IsIncluded))
        {
            SaveCurrentDraft();
        }
    }

    private static bool IsExpectedUserFacingException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or StateFileException
            or DbException
            or HttpRequestException
            or System.Text.Json.JsonException;
}
