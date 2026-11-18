using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using DocTwin.Analysis;
using DocTwin.Confluence;
using DocTwin.Security;
using DocTwin.ViewModels;

namespace DocTwin;

public partial class ConfluenceWindow : Window
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _aiHttpClient;
    private readonly ConfluenceViewModel _viewModel;

    public ConfluenceWindow() : this(null) { }

    public ConfluenceWindow(ConfluenceViewModel? viewModel)
    {
        _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _aiHttpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        var client = new ConfluenceClient(_httpClient, new PageContentExtractor());
        var pageCache = new PageCache();
        var cachingClient = new CachingConfluenceClient(client, client, pageCache);
        _viewModel = viewModel ?? new ConfluenceViewModel(client, new WindowsCredentialStore(), new PageComparison(),
            new DuplicateDiscovery(cachingClient, new ConfluenceDiscovery(client)),
            new DocTwin.AI.ProviderSwitchingAiComparison(_aiHttpClient), pageCache: pageCache);
        InitializeComponent();
        DataContext = _viewModel;
        if (viewModel == null)
        {
            _viewModel.LoadSavedConnection();
            _viewModel.LoadReviewHistory();
            _viewModel.LoadAiSettings();
            _viewModel.RefreshCacheStatus();
        }
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e) => Themes.ThemeService.Current.ToggleCommand.Execute(null);

    private void HelpButton_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfluenceViewModel.Site) or nameof(ConfluenceViewModel.Email)
            or nameof(ConfluenceViewModel.ScopedToken) or nameof(ConfluenceViewModel.CloudId))
            TokenBox.Clear();
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfluenceViewModel viewModel)
            viewModel.SetToken(TokenBox.Password);
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Remove DocTwin's saved connection and token from this Windows account? This does not revoke the token in Atlassian.",
            "Remove saved connection", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            TokenBox.Clear();
            _viewModel.RemoveCommand.Execute(null);
        }
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e) => OpenPage(_viewModel.SourceLink);
    private void OpenCandidateButton_Click(object sender, RoutedEventArgs e) => OpenPage(_viewModel.CandidateLink);

    private void OpenPage(Uri? uri)
    {
        if (uri == null)
        {
            MessageBox.Show(this, "Retrieve and compare the pages first.", "Page not loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Win32Exception)
        {
            MessageBox.Show(this, "Windows could not open your browser. Open the original page URL in your browser manually.",
                "Browser unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            _viewModel.CancelCommand.Execute(null);
            e.Cancel = true;
            _viewModel.Status = "Cancelling the active request. Close this window again once cancellation completes.";
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        TokenBox.Clear();
        _viewModel.ForgetSessionToken();
        _httpClient.Dispose();
        _aiHttpClient.Dispose();
        base.OnClosed(e);
    }
}
