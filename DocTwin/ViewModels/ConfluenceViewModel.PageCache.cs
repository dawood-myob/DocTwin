using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DocTwin.ViewModels;

public partial class ConfluenceViewModel
{
    [ObservableProperty] private string _cacheStatus = string.Empty;

    public void RefreshCacheStatus() => CacheStatus = _pageCache.Describe() + (_pageCache.LoadWarning is { } warning ? " " + warning : string.Empty);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void ClearCache()
    {
        try
        {
            _pageCache.Clear();
            RefreshCacheStatus();
            Status = "Page cache cleared. The next scan starts from scratch and reads every page from Confluence. Review decisions and settings were not affected.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "The page cache file could not be deleted. Close other programs using %LOCALAPPDATA%\\DocTwin\\page-cache.bin and try again.";
        }
    }

    private async Task SaveCacheAsync()
    {
        try { await Task.Run(_pageCache.Flush); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            ScanWarnings += (ScanWarnings.Length == 0 ? string.Empty : "\n") + "The page cache could not be saved, so the next scan will re-read pages. Scan results are unaffected.";
        }
        RefreshCacheStatus();
    }
}
