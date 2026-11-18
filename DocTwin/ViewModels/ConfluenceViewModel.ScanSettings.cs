using System.Globalization;
using System.IO;
using System.Text.Json;
using DocTwin.Persistence;

namespace DocTwin.ViewModels;

public partial class ConfluenceViewModel
{
    private readonly ScanSettingsStore _scanSettings = new();
    private bool _restoringSettings;

    // A remembered number above the allowed maximum is brought down to it, so an old value can't block the next scan.
    private static string ClampLimit(string saved, int max) =>
        int.TryParse(saved, out int value) && value > max ? max.ToString(CultureInfo.InvariantCulture) : saved;

    private void ApplyScanSettings(bool restoreAuthorisation)
    {
        _restoringSettings = true;
        try
        {
            var saved = _scanSettings.Read();
            if (saved == null)
            {
                // A saved credential can only exist if both boxes were ticked when it was saved.
                if (restoreAuthorisation) { Authorised = true; SaveConsent = true; }
                return;
            }
            RootUrl = saved.RootUrl;
            SearchSpaceKeys = saved.SearchSpaceKeys;
            AllAccessibleSpaces = saved.AllAccessibleSpaces;
            SourceLimit = ClampLimit(saved.SourceLimit, 1000);
            ExternalLimit = ClampLimit(saved.ExternalLimit, 5000);
            LexicalThreshold = saved.LexicalThreshold;
            SelectedScanMode = saved.Mode;
            AiIncludeThin = saved.AiThin;
            AiIncludeStale = saved.AiStale;
            AiIncludeDuplicate = saved.AiDuplicate;
            AiIncludeHealthy = saved.AiHealthy;
            // The tick boxes are only restored alongside a saved credential, which was itself saved after the user confirmed them.
            if (restoreAuthorisation) { Authorised = saved.Authorised; SaveConsent = saved.SaveConsent; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Status = "Saved scan settings could not be loaded. Enter them again; existing settings were not overwritten.";
        }
        finally { _restoringSettings = false; }
    }

    private void RememberChoice()
    {
        if (!_restoringSettings) RememberScanSettings();
    }

    private void RememberScanSettings()
    {
        try
        {
            _scanSettings.Save(new ScanSettings(RootUrl.Trim(), SearchSpaceKeys.Trim(), AllAccessibleSpaces, SourceLimit.Trim(),
                ExternalLimit.Trim(), LexicalThreshold.Trim(), SelectedScanMode, Authorised, SaveConsent,
                AiIncludeThin, AiIncludeStale, AiIncludeDuplicate, AiIncludeHealthy));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Remembering the setup is a convenience; a failure must never block saving or scanning.
        }
    }
}
