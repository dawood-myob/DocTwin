using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocTwin.AI;
using DocTwin.Analysis;

namespace DocTwin.ViewModels;

public partial class ConfluenceViewModel
{
    private AiQualityPlan _aiQualityPlan = new();
    private IReadOnlyList<DiscoveredPage> _scanSourcePages = [];

    public ObservableCollection<AiPageQuality> AiQualityRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAiQualityReviewCommand))]
    private bool _aiQualityConsent;

    [ObservableProperty] private string _aiQualitySummary = "No AI quality review yet. Run discovery, check the plan, approve sharing and start the review.";

    public string AiQualityPlanText => Uri.TryCreate(AiEndpoint, UriKind.Absolute, out var endpoint)
        ? _aiQualityPlan.Describe(new AiConnection(SelectedAiProvider, AiModel.Trim(), endpoint, AiKeyVariable.Trim()))
        : "Enter a valid AI endpoint on the AI tab before planning a review.";

    public string AiQualityReadiness => IsBusy ? "An operation is running. Wait for it to finish or choose Cancel."
        : _qualityAdvisor == null ? "AI quality review is not available in this build."
        : _aiQualityPlan.Pages.Count == 0 ? (_scanSourcePages.Count == 0 ? "Run discovery first; there are no pages to review yet."
            : _aiQualityPlan.SensitivePages.Count > 0 && _aiQualityPlan.ExcludedByScope == 0 ? "Every reviewable page contains possible secrets, which are never sent to AI. Remove them in Confluence and scan again."
            : "Tick at least one matching page type above; there are no pages to review in the current selection.")
        : !AiQualityConsent ? "Review the plan above, check the provider on the AI tab, then approve sharing to enable the review."
        : "Sharing approved for exactly the pages in the plan. Choose Run AI quality review.";

    private void PrepareAiQualityPlan(DiscoveryResults results)
    {
        _scanSourcePages = results.SourcePages.ToArray();
        RebuildAiQualityPlan();
    }

    // Statuses the user wants reviewed; a page qualifies if any of its content-health statuses is ticked.
    [ObservableProperty] private bool _aiIncludeThin = true;
    [ObservableProperty] private bool _aiIncludeStale = true;
    [ObservableProperty] private bool _aiIncludeDuplicate = true;
    [ObservableProperty] private bool _aiIncludeHealthy = true;

    public string AiThinLabel => $"Thin ({_health?.ThinPages ?? 0})";
    public string AiStaleLabel => $"Stale ({_health?.StalePages ?? 0})";
    public string AiDuplicateLabel => $"In a duplicate group ({_health?.DuplicatePages ?? 0})";
    public string AiHealthyLabel => $"Healthy ({_health?.HealthyPages ?? 0})";

    partial void OnAiIncludeThinChanged(bool value) => ScopeChanged();
    partial void OnAiIncludeStaleChanged(bool value) => ScopeChanged();
    partial void OnAiIncludeDuplicateChanged(bool value) => ScopeChanged();
    partial void OnAiIncludeHealthyChanged(bool value) => ScopeChanged();

    private void ScopeChanged()
    {
        RebuildAiQualityPlan();
        RememberChoice();
    }

    private void RebuildAiQualityPlan()
    {
        _aiQualityPlan = AiQualityPlan.Create(_scanSourcePages, _health,
            scope: new AiQualityScope(AiIncludeThin, AiIncludeStale, AiIncludeDuplicate, AiIncludeHealthy));
        AiQualityConsent = false;
        RefreshAiQuality();
    }

    private void RefreshAiQuality()
    {
        OnPropertyChanged(nameof(AiThinLabel));
        OnPropertyChanged(nameof(AiStaleLabel));
        OnPropertyChanged(nameof(AiDuplicateLabel));
        OnPropertyChanged(nameof(AiHealthyLabel));
        OnPropertyChanged(nameof(AiQualityPlanText));
        OnPropertyChanged(nameof(AiQualityReadiness));
        RunAiQualityReviewCommand.NotifyCanExecuteChanged();
    }

    // Any change to the destination, model, key or scan invalidates earlier consent.
    private void ResetAiQualityConsent()
    {
        AiQualityConsent = false;
        RefreshAiQuality();
    }

    partial void OnAiQualityConsentChanged(bool value) => OnPropertyChanged(nameof(AiQualityReadiness));

    private bool CanRunAiQualityReview() => !IsBusy && AiQualityConsent && _qualityAdvisor != null && _aiQualityPlan.Pages.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRunAiQualityReview))]
    private async Task RunAiQualityReviewAsync()
    {
        AiQualityRows.Clear();
        AiQualitySummary = "AI quality review in progress. Results appear as each page is accepted.";
        var plan = _aiQualityPlan;
        var outcome = new AiQualityOutcome();
        await RunAsync(async cancellation =>
        {
            if (!AiQualityConsent || _qualityAdvisor == null)
                throw new ArgumentException("Approve sharing the planned pages with the selected AI provider first.");
            if (!Uri.TryCreate(AiEndpoint, UriKind.Absolute, out var endpoint))
                throw new ArgumentException("Enter a valid AI API endpoint URL.");
            var connection = new AiConnection(SelectedAiProvider, AiModel.Trim(), endpoint, AiKeyVariable.Trim());
            AiSettingsStore.Validate(new AiProfile(connection.Provider, connection.Model, connection.Endpoint.OriginalString, connection.ApiKeyEnvironmentVariable));
            bool accepting = true;
            try
            {
                var pageProgress = new Progress<AiPageQuality>(row => { if (accepting) AiQualityRows.Add(row); });
                var statusProgress = new Progress<string>(text => { if (accepting && IsBusy) Status = text; });
                var result = await AiTreeQualityReview.RunAsync(_qualityAdvisor, connection, plan, pageProgress, statusProgress, cancellation);
                outcome.Results.AddRange(result.Results);
                outcome.StoppedEarly = result.StoppedEarly;
                outcome.StopReason = result.StopReason;
            }
            finally { accepting = false; }
            // Progress callbacks are posted asynchronously, so the final list is the authoritative set of rows.
            AiQualityRows.Clear();
            foreach (var row in outcome.Results) AiQualityRows.Add(row);
            AiQualitySummary = outcome.Summary();
            Status = "AI quality review finished. These are advisory suggestions; no Confluence content was changed.";
        }, "Requesting AI quality review of the planned pages...", 60);
        if (AiQualitySummary.StartsWith("AI quality review in progress", StringComparison.Ordinal))
            AiQualitySummary = $"The AI quality review did not complete. {AiQualityRows.Count} pages were accepted before it stopped. {Status}";
        ExportReportCommand.NotifyCanExecuteChanged();
        RefreshAiQuality();
    }
}
