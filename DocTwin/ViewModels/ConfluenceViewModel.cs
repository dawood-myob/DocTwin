using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocTwin.Analysis;
using DocTwin.Confluence;
using DocTwin.Diagnostics;
using DocTwin.Security;
using Serilog;
using Serilog.Context;

namespace DocTwin.ViewModels;

public partial class ConfluenceViewModel : ObservableObject
{
    private readonly IConfluenceClient _client;
    private readonly ICredentialStore _credentials;
    private readonly PageComparison _comparison;
    private string _token = string.Empty;
    private ConnectionProfile? _validatedProfile;
    private CancellationTokenSource? _operation;
    private readonly DuplicateDiscovery _discovery;
    private readonly DocTwin.AI.IAiComparisonProvider _ai;
    private readonly DocTwin.AI.IAiQualityAdvisor? _qualityAdvisor;

    public ConfluenceViewModel(IConfluenceClient client, ICredentialStore credentials, PageComparison comparison,
        DuplicateDiscovery discovery, DocTwin.AI.IAiComparisonProvider ai, ReviewStore? reviewStore = null,
        DocTwin.AI.IAiQualityAdvisor? qualityAdvisor = null)
    {
        _reviewStore = reviewStore ?? new ReviewStore();
        _qualityAdvisor = qualityAdvisor ?? ai as DocTwin.AI.IAiQualityAdvisor;
        _client = client;
        _credentials = credentials;
        _comparison = comparison;
        _discovery = discovery;
        _ai = ai;
    }

    [ObservableProperty] private string _site = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private bool _scopedToken = true;
    [ObservableProperty] private string _cloudId = string.Empty;
    [ObservableProperty] private bool _authorised;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _saveConsent;
    [ObservableProperty] private string _sourceUrl = string.Empty;
    [ObservableProperty] private string _candidateUrl = string.Empty;
    [ObservableProperty] private string _status = "Enter your connection details. No requests are sent until you test or compare.";
    [ObservableProperty] private string _credentialStatus = "No saved connection loaded.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(AiReadiness))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewFindingCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrepareAiPairCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveReviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    [NotifyPropertyChangedFor(nameof(AiQualityReadiness))]
    [NotifyCanExecuteChangedFor(nameof(RunAiQualityReviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAiSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportAiSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isConnected;
    [ObservableProperty] private string _sourceDetails = "Source page has not been retrieved.";
    [ObservableProperty] private string _candidateDetails = "Candidate page has not been retrieved.";
    [ObservableProperty] private string _summary = "No comparison yet. Enter two different page URLs from the configured site.";
    [ObservableProperty] private string _limitations = string.Empty;

    public bool IsIdle => !IsBusy;
    public ObservableCollection<ComparisonRow> Rows { get; } = [];
    public Uri? SourceLink { get; private set; }
    public Uri? CandidateLink { get; private set; }

    public void LoadSavedConnection()
    {
        try
        {
            var saved = _credentials.Read();
            if (saved == null)
            {
                ApplyScanSettings(restoreAuthorisation: false);
                CredentialStatus = "No saved connection. Token will remain in memory unless you explicitly save it.";
                return;
            }
            var profile = ConnectionProfile.Create(saved.Site, saved.Email, saved.ScopedToken, saved.CloudId);
            Site = profile.Site.AbsoluteUri;
            Email = profile.Email;
            ScopedToken = profile.ScopedToken;
            CloudId = profile.CloudId;
            ApplyScanSettings(restoreAuthorisation: true);
            _token = saved.Token;
            CredentialStatus = "Saved token loaded securely from Windows Credential Manager. It is not displayed; leave the token box empty and go straight to scanning.";
            Status = "Saved connection and scan settings restored. Open Discover duplicates and run the scan; the connection is re-validated automatically.";
        }
        catch (Win32Exception)
        {
            Status = "Could not read Windows Credential Manager. Enter a token for this session or try again later.";
        }
        catch (JsonException)
        {
            Status = "Saved connection is unreadable. Remove it and enter a new connection.";
        }
        catch (ArgumentException)
        {
            Status = "Saved connection has invalid details. Remove it and enter a new connection.";
        }
    }

    public void SetToken(string token)
    {
        _token = token;
        InvalidateConnection();
        CredentialStatus = "New token entered for this session. Test before saving; the existing saved credential has not been changed.";
    }

    public void ForgetSessionToken() => _token = string.Empty;

    partial void OnSiteChanged(string value) => ConnectionDetailsChanged();
    partial void OnEmailChanged(string value) => ConnectionDetailsChanged();
    partial void OnScopedTokenChanged(bool value) => ConnectionDetailsChanged();
    partial void OnCloudIdChanged(string value) => ConnectionDetailsChanged();
    partial void OnAuthorisedChanged(bool value) => InvalidateConnection();
    partial void OnSourceUrlChanged(string value) => ClearResults();
    partial void OnCandidateUrlChanged(string value) => ClearResults();

    private void ConnectionDetailsChanged()
    {
        _token = string.Empty;
        CredentialStatus = "Connection details changed. Re-enter your API token; any saved credential is unchanged.";
        InvalidateConnection();
    }

    private void InvalidateConnection()
    {
        IsConnected = false;
        _validatedProfile = null;
        ClearResults();
        Findings.Clear();
        SourcePages.Clear();
        SourceTree.Clear();
        SourceTreeDetails = "Connection changed. Run discovery to inspect the current account's source tree.";
        SelectedFinding = null;
        ScanCoverage = "Connection changed. Run discovery again with the current authorised account.";
        Status = "Connection not tested with the current details.";
    }

    private void ClearResults()
    {
        _currentSource = null;
        _currentCandidate = null;
        _currentSourcePath = null;
        _currentRootId = null;
        _currentRootTitle = null;
        AiConsent = false;
        ActivePairDetails = "No page pair loaded. Select a discovery finding and open it for review, or fetch two live page URLs.";
        OnPropertyChanged(nameof(AiReadiness));
        AiOutput = "No AI analysis for the current pair.";
        AnalyseCommand.NotifyCanExecuteChanged();
        Rows.Clear();
        SourceLink = null;
        CandidateLink = null;
        SourceDetails = "Source page has not been retrieved.";
        CandidateDetails = "Candidate page has not been retrieved.";
        Summary = "No current comparison. Compare the selected pages to retrieve fresh content.";
        Limitations = string.Empty;
    }

    private bool CanStart() => !IsBusy;
    private bool CanSave() => !IsBusy && IsConnected && SaveConsent;
    private bool CanCancel() => IsBusy;

    private ConnectionProfile GetProfile()
    {
        if (!Authorised)
            throw new ArgumentException("Confirm that your organisation permits this token-based connection and that you are authorised to access these pages.");
        return ConnectionProfile.Create(Site, Email, ScopedToken, CloudId);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task TestAsync()
    {
        IsConnected = false;
        _validatedProfile = null;
        await RunAsync(async cancellation =>
        {
            var profile = GetProfile();
            await _client.TestConnectionAsync(profile, _token, cancellation);
            _validatedProfile = profile;
            IsConnected = true;
            Status = $"Connection validated at {DateTimeOffset.Now:g}. Identity and Confluence REST v2 read access confirmed.";
        }, "Testing authentication and page-read access...");
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CanSave() || _validatedProfile == null)
            return;
        try
        {
            _credentials.Save(_validatedProfile, _token);
            RememberScanSettings();
            CredentialStatus = "Connection saved in Windows Credential Manager for this Windows account. The token is not displayed.";
            Status = "Validated connection saved securely. You can now compare live Confluence pages.";
        }
        catch (Win32Exception)
        {
            Status = "Windows Credential Manager could not save the connection. It is available for this session only.";
        }
        catch (ArgumentException ex)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Remove()
    {
        try
        {
            _credentials.Remove();
            _token = string.Empty;
            InvalidateConnection();
            CredentialStatus = "Saved connection and session token removed. Revoke the API token in Atlassian separately if required.";
            Status = "Saved connection removed from this machine.";
        }
        catch (Win32Exception)
        {
            Status = "Windows Credential Manager could not remove the saved connection. Nothing was reported as removed.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task CompareAsync()
    {
        ClearResults();
        await RunAsync(async cancellation =>
        {
            var profile = GetProfile();
            var sourceId = profile.ResolvePageId(SourceUrl);
            var candidateId = profile.ResolvePageId(CandidateUrl);
            if (sourceId.TrimStart('0') == candidateId.TrimStart('0'))
                throw new ArgumentException("Choose two different Confluence pages; a page cannot be compared against itself.");

            if (!IsConnected)
            {
                await _client.TestConnectionAsync(profile, _token, cancellation);
                _validatedProfile = profile;
                IsConnected = true;
            }
            Status = "Retrieving source page (read-only)...";
            var source = await _client.GetPageAsync(profile, _token, sourceId, cancellation);
            Status = "Retrieving candidate page (read-only)...";
            var candidate = await _client.GetPageAsync(profile, _token, candidateId, cancellation);
            Status = "Comparing extracted Confluence content locally...";
            var result = await Task.Run(() => _comparison.Compare(source, candidate), cancellation);
            cancellation.ThrowIfCancellationRequested();
            PresentComparison(source, candidate, result);
            Status = $"Live comparison completed at {DateTimeOffset.Now:g}. Only the two requested pages were compared. No Confluence content was changed.";
        }, "Preparing live Confluence comparison...");
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operation?.Cancel();

    private async Task RunAsync(Func<CancellationToken, Task> action, string message, int deadlineMinutes = 3,
        [CallerMemberName] string operationName = "")
    {
        using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(deadlineMinutes));
        string reference = Guid.NewGuid().ToString("N");
        using var correlation = LogContext.PushProperty("CorrelationId", reference);
        var timer = Stopwatch.StartNew();
        string outcome = "Failed";
        _operation = operation;
        IsBusy = true;
        Status = message;
        Log.Information("OperationStarted {Operation}", operationName);
        try { await action(operation.Token); outcome = "Completed"; }
        catch (OperationCanceledException)
        {
            outcome = "CancelledOrTimedOut";
            Status = operation.IsCancellationRequested ? "Operation cancelled or its deadline expired. No completed result was assumed; any retained scan results are partial." :
                "The request timed out. Check your connection and try again.";
        }
        catch (ConfluenceException ex) { Status = ex.Message; IsConnected = false; }
        catch (ArgumentException ex) { Status = ex.Message; }
        catch (HttpRequestException) { Status = "Network, TLS or response-size failure. Check your connection, VPN and provider/site settings. No page content or token is logged."; IsConnected = false; }
        catch (JsonException) { Status = "A service returned malformed or unsupported JSON. No new analysis was accepted. Check its configuration and permissions."; }
        catch (InvalidDataException ex) { Status = ex.Message; }
        catch (TimeoutException) { Status = "AI runtime timed out. No AI findings were accepted; the local comparison remains available."; }
        catch (UnauthorizedAccessException) { Status = "AI runtime access was denied. Check provider sign-in and organisation permissions."; }
        finally
        {
            Log.Information("OperationFinished {Operation} {Outcome} {ElapsedMilliseconds}", operationName, outcome, timer.ElapsedMilliseconds);
            if (outcome != "Completed") Status += $" Support reference: {reference}.";
            if (AppDiagnostics.ConsumeWriteFailure()) Status += " Local diagnostics could not be written. Check available disk space and access to the DocTwin logs folder.";
            _operation = null;
            IsBusy = false;
        }
    }
}
