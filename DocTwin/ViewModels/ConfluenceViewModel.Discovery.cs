using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocTwin.AI;
using DocTwin.Analysis;
using DocTwin.Confluence;

namespace DocTwin.ViewModels;

public partial class ConfluenceViewModel
{
    private ConfluencePage? _currentSource;
    private ConfluencePage? _currentCandidate;
    private string? _currentSourcePath;
    private string? _currentRootId;
    private string? _currentRootTitle;
    [ObservableProperty] private bool _isScanSettingsExpanded = true;
    [ObservableProperty] private string _rootUrl = string.Empty;
    [ObservableProperty] private string _searchSpaceKeys = string.Empty;
    [ObservableProperty] private bool _allAccessibleSpaces;
    [ObservableProperty] private string _sourceLimit = "500";
    [ObservableProperty] private string _externalLimit = "1000";
    [ObservableProperty] private string _lexicalThreshold = "0.5";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScanModeDescription))]
    private DiscoveryMode _selectedScanMode = DiscoveryMode.Quick;
    public IReadOnlyList<DiscoveryMode> ScanModes { get; } = Enum.GetValues<DiscoveryMode>();
    public string ScanModeDescription => SelectedScanMode == DiscoveryMode.Quick
        ? $"Quick: traverse the source tree, search its titles/identifiers across the approved spaces, then prioritise up to {DuplicateDiscovery.QuickPageLimit} candidate bodies within a {DuplicateDiscovery.QuickMetadataLimit:N0}-entry metadata budget (or your lower body limit). Coverage is partial."
        : "Deep: search source titles/identifiers first, then systematically read the remaining external inventory up to your configured limit. Starts a fresh scan; may take longer. Neither mode guarantees complete coverage.";
    [ObservableProperty] private string _scanCoverage = "No discovery scan yet. Set a root URL and an approved cross-space search scope.";
    [ObservableProperty] private string _scanWarnings = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewFindingCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareAiPairCommand))]
    private DuplicateFinding? _selectedFinding;
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private AiProvider _selectedAiProvider = AiProvider.GitHubCopilot;
    [ObservableProperty] private string _aiModel = "gpt-4.1";
    [ObservableProperty] private string _aiEndpoint = "https://api.openai.com/v1/chat/completions";
    [ObservableProperty] private string _aiKeyVariable = "DOCTWIN_OPENAI_API_KEY";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyseCommand))]
    [NotifyPropertyChangedFor(nameof(AiReadiness))]
    private bool _aiConsent;
    [ObservableProperty] private string _activePairDetails = "No page pair loaded. Select a discovery finding and open it for review, or fetch two live page URLs.";
    public string AiReadiness => IsBusy
        ? "An operation is running. Wait for it to finish or choose Cancel before starting AI analysis."
        : _currentSource == null || _currentCandidate == null
        ? "Load a page pair first. Opening this tab alone does not select or send any content."
        : !AiConsent ? "Pair loaded. Review the provider settings and approve sharing using the checkbox above the Analyse button."
        : "Pair loaded and sharing approved. Choose Analyse selected pair with AI to send this pair.";
    [ObservableProperty] private string _aiOutput = "No AI analysis for the current pair.";

    public ObservableCollection<DuplicateFinding> Findings { get; } = [];
    public ObservableCollection<DiscoveredPage> SourcePages { get; } = [];
    public ObservableCollection<SourceTreeItem> SourceTree { get; } = [];
    [ObservableProperty] private string _sourceTreeDetails = "Select a node to inspect its context and extracted key-text preview.";
    public IReadOnlyList<AiProvider> AiProviders { get; } = Enum.GetValues<AiProvider>();
    public bool UsesApiKey => SelectedAiProvider != AiProvider.GitHubCopilot;

    partial void OnRootUrlChanged(string value) => ClearScan();
    partial void OnSearchSpaceKeysChanged(string value) => ClearScan();
    partial void OnAllAccessibleSpacesChanged(bool value) => ClearScan();
    partial void OnSourceLimitChanged(string value) => ClearScan();
    partial void OnExternalLimitChanged(string value) => ClearScan();
    partial void OnLexicalThresholdChanged(string value) => ClearScan();
    partial void OnSelectedScanModeChanged(DiscoveryMode value) => ClearScan();

    private void ClearScan()
    {
        Findings.Clear();
        SourcePages.Clear();
        SourceTree.Clear();
        SourceTreeDetails = "Select a node to inspect its context and extracted key-text preview.";
        SelectedFinding = null;
        ScanCoverage = "Scan configuration changed. Run discovery to obtain results for this scope.";
        ScanWarnings = string.Empty;
    }

    partial void OnSelectedAiProviderChanged(AiProvider value)
    {
        AiConsent = false;
        AiOutput = "AI provider changed. Review the endpoint, model, credential source and data-sharing consent before requesting analysis.";
        AiModel = value switch { AiProvider.Claude => "claude-sonnet-4-6", _ => "gpt-4.1" };
        AiEndpoint = value == AiProvider.Claude ? "https://api.anthropic.com/v1/messages" : "https://api.openai.com/v1/chat/completions";
        AiKeyVariable = value == AiProvider.Claude ? "ANTHROPIC_API_KEY" : "DOCTWIN_OPENAI_API_KEY";
        ApplySavedAiProfile();
        OnPropertyChanged(nameof(UsesApiKey));
    }
    partial void OnAiEndpointChanged(string value) { AiConsent = false; AiOutput = "AI endpoint changed. Review the destination and approve data sharing again."; }
    partial void OnAiModelChanged(string value) { AiConsent = false; AiOutput = "AI model changed. Request a new analysis for this model."; }
    partial void OnAiKeyVariableChanged(string value) { AiConsent = false; }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task ScanAsync()
    {
        ClearScan();
        ClearResults();
        var results = new DiscoveryResults();
        await RunAsync(async cancellation =>
        {
            var profile = GetProfile();
            string rootId = profile.ResolvePageId(RootUrl);
            var spaces = SearchSpaceKeys.Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!AllAccessibleSpaces && spaces.Length == 0)
                throw new ArgumentException("Enter one or more external space keys (for example MSS, PB), or explicitly select All accessible spaces. The search is not restricted to the root's space.");
            if (!int.TryParse(SourceLimit, out int sourceLimit) || !int.TryParse(ExternalLimit, out int externalLimit) ||
                !double.TryParse(LexicalThreshold, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double threshold))
                throw new ArgumentException("Enter whole-number page limits and a decimal lexical threshold such as 0.5.");
            if (sourceLimit is < 1 or > 1000 || externalLimit is < 1 or > 5000 || !double.IsFinite(threshold) || threshold is < 0.1 or > 1)
                throw new ArgumentException("Source nodes must be 1-1,000; external pages 1-5,000; lexical threshold 0.1-1.0.");
            IsScanSettingsExpanded = false;
            await _client.TestConnectionAsync(profile, _token, cancellation);
            IsConnected = true;
            _validatedProfile = profile;
            bool acceptingProgress = true;
            try
            {
                var progress = new Progress<string>(text => { if (acceptingProgress && IsBusy) Status = text; });
                var findingProgress = new Progress<DuplicateFinding>(finding =>
                {
                    if (!acceptingProgress || !IsBusy) return;
                    Findings.Add(finding);
                    SelectedFinding ??= finding;
                });
                var scope = AllAccessibleSpaces ? Array.Empty<string>() : spaces;
                string token = _token;
                await Task.Run(() => _discovery.ScanAsync(profile, token, rootId, scope,
                    sourceLimit, externalLimit, threshold, results, progress, cancellation, SelectedScanMode, findingProgress), cancellation);
                Status = results.SourceComplete && results.ExternalComplete && results.FailedPages == 0 ?
                    "Discovery completed for the accessible published pages in the approved scope. Review findings; similarity is not proof of redundancy." :
                    "Discovery finished with incomplete coverage or skipped pages. Read coverage and warnings before interpreting the findings.";
            }
            finally
            {
                acceptingProgress = false;
                var selection = SelectedFinding;
                Findings.Clear();
                foreach (var page in results.SourcePages)
                    SourcePages.Add(page);
                var pagesById = results.SourcePages.ToDictionary(x => x.Page.Id, x => x.Page);
                var nodes = results.HierarchyNodes.ToDictionary(x => x.Id, x => new SourceTreeItem(x.Id, x.Title, x.Type, pagesById.GetValueOrDefault(x.Id)));
                foreach (var node in results.HierarchyNodes)
                    if (node.ParentId != null && nodes.TryGetValue(node.ParentId, out var parent)) parent.Children.Add(nodes[node.Id]);
                    else SourceTree.Add(nodes[node.Id]);
                foreach (var finding in results.Findings.OrderByDescending(x => x.Relationship.StartsWith("Exact", StringComparison.Ordinal))
                    .ThenByDescending(x => x.LexicalScore).ThenByDescending(x => x.SmallerPageCoverage))
                    Findings.Add(finding);
                SelectedFinding = selection != null && Findings.Contains(selection) ? selection : Findings.FirstOrDefault();
                ScanCoverage = $"{results.Mode} scan. Title searches: {results.TargetedQueries}; title-search candidates: {results.TargetedCandidates}; search errors: {results.SearchFailures}. External body attempts: {results.ExternalAttempts}; " +
                    (results.Mode == DiscoveryMode.Quick ? $"external metadata inspected: {results.MetadataPages}. " : string.Empty) +
                    $"Source pages analysed: {results.SourcePages.Count}; external pages analysed: {results.ExternalPages}; candidate pairs: {results.Findings.Count}. {results.SkipSummary}. " +
                    $"Source hierarchy enumerated: {results.SourceHierarchyComplete}; source content complete: {results.SourceContentComplete}; external inventory exhausted: {results.ExternalComplete}. " +
                    "Invisible/restricted pages cannot be counted. Enumeration is not a point-in-time site snapshot. No matches does not prove absence of redundant information.";
                ScanWarnings = string.Join("\n", results.Warnings);
            }
        }, "Starting root-tree and cross-space duplicate discovery...", 60);
    }

    private bool CanReviewFinding() => !IsBusy && SelectedFinding != null;

    [RelayCommand(CanExecute = nameof(CanReviewFinding))]
    private async Task ReviewFindingAsync()
    {
        var finding = SelectedFinding;
        if (finding == null)
            return;
        ClearResults();
        await RunAsync(async cancellation =>
        {
            var result = await Task.Run(() => _comparison.Compare(finding.Source, finding.Candidate), cancellation);
            cancellation.ThrowIfCancellationRequested();
            SourceUrl = finding.Source.Url.AbsoluteUri;
            CandidateUrl = finding.Candidate.Url.AbsoluteUri;
            PresentComparison(finding.Source, finding.Candidate, result);
            _currentSourcePath = finding.SourcePath;
            _currentRootId = finding.SourceRootId;
            _currentRootTitle = finding.SourceRootTitle;
            SelectedTab = 2;
            Status = "Showing evidence captured during the scan. Use Fetch & compare to refresh, or request optional AI analysis for this pair.";
        }, "Preparing the selected discovery finding...");
    }

    [RelayCommand(CanExecute = nameof(CanReviewFinding))]
    private async Task PrepareAiPairAsync()
    {
        await ReviewFindingAsync();
        if (_currentSource != null && _currentCandidate != null)
            SelectedTab = 3;
    }

    private void PresentComparison(ConfluencePage source, ConfluencePage candidate, PageComparisonResult result)
    {
        _currentSource = source;
        _currentCandidate = candidate;
        ActivePairDetails = $"Loaded pair: {source.Title} (v{source.Version}) / {candidate.Title} (v{candidate.Version}). AI uses these captured texts, not a whole-site scan.";
        OnPropertyChanged(nameof(AiReadiness));
        SourceDetails = $"{source.Title}\nPage {source.Id} | Space {source.SpaceId} | Version {source.Version}\nUpdated: {source.Updated}";
        CandidateDetails = $"{candidate.Title}\nPage {candidate.Id} | Space {candidate.SpaceId} | Version {candidate.Version}\nUpdated: {candidate.Updated}";
        SourceLink = source.Url;
        CandidateLink = candidate.Url;
        Rows.Clear();
        foreach (var row in result.Rows)
            Rows.Add(row);
        Summary = (result.Identical ? "Identical extracted text. " : "The extracted page texts differ. ") +
            $"Changed line entries: {result.ChangedLines} of {result.TotalLines} across both pages. This is not a semantic similarity score or a source-of-truth decision.";
        Limitations = string.Join("\n", source.Limitations.Concat(candidate.Limitations).Distinct());
        AiOutput = "No AI analysis for the current pair.";
        AnalyseCommand.NotifyCanExecuteChanged();
    }

    private bool CanAnalyse() => !IsBusy && AiConsent && _currentSource != null && _currentCandidate != null;

    [RelayCommand(CanExecute = nameof(CanAnalyse))]
    private async Task AnalyseAsync()
    {
        AiOutput = "No accepted AI result yet. Request in progress...";
        await RunAsync(async cancellation =>
        {
            if (!AiConsent || _currentSource == null || _currentCandidate == null)
                throw new ArgumentException("Select a finding or retrieve two pages, then approve sharing them with the selected AI provider.");
            if (!Uri.TryCreate(AiEndpoint, UriKind.Absolute, out var endpoint))
                throw new ArgumentException("Enter a valid AI API endpoint URL.");
            var result = await _ai.CompareAsync(new AiComparisonRequest(SelectedAiProvider, AiModel.Trim(), endpoint,
                AiKeyVariable.Trim(), _currentSource, _currentCandidate)
            {
                SourcePath = _currentSourcePath, SourceRootId = _currentRootId, SourceRootTitle = _currentRootTitle
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var output = new StringBuilder();
            output.AppendLine($"ADVISORY AI ANALYSIS | {SelectedAiProvider} | {AiModel} | {DateTimeOffset.Now:g}");
            output.AppendLine($"Relationship: {result.Relationship}\n{result.Summary}\nSuggested human review: {result.SuggestedAction}\n");
            foreach (var item in result.Evidence)
                output.AppendLine($"{item.Kind}: {item.Explanation}\nSource quote: {item.SourceQuote}\nCandidate quote: {item.CandidateQuote}\n");
            output.AppendLine("Uncertainties: " + string.Join("; ", result.Uncertainties));
            output.AppendLine("Quotations were checked against the extracted texts; explanations still require human verification. No Confluence content was changed.");
            AiOutput = output.ToString();
            Status = "AI analysis received and evidence quotations validated. This is an advisory result, not a canonical-page or deletion decision.";
        }, "Requesting optional AI analysis of the selected page pair...");
        if (AiOutput == "No accepted AI result yet. Request in progress...")
            AiOutput = "No AI analysis was accepted. " + Status;
    }
}
