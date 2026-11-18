using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using DocTwin.AI;
using Microsoft.Win32;

namespace DocTwin.ViewModels;

public partial class ConfluenceViewModel
{
    private readonly AiSettingsStore _aiSettings = new();
    private readonly Dictionary<AiProvider, AiProfile> _aiProfiles = [];

    public string AiKeyStatus => AiCredentials.Describe(SelectedAiProvider, AiKeyVariable.Trim());

    [RelayCommand]
    private void RecheckAiKey() => OnPropertyChanged(nameof(AiKeyStatus));

    public void LoadAiSettings()
    {
        try
        {
            foreach (var profile in _aiSettings.Read()) _aiProfiles[profile.Provider] = profile;
            ApplySavedAiProfile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Status = "AI settings could not be loaded. Default templates are displayed; review them before consenting. Existing settings have not been overwritten.";
        }
    }

    private void ApplySavedAiProfile()
    {
        if (!_aiProfiles.TryGetValue(SelectedAiProvider, out var profile)) return;
        AiModel = profile.Model;
        AiEndpoint = profile.Endpoint;
        AiKeyVariable = profile.ApiKeyEnvironmentVariable;
        AiConsent = false;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void SaveAiSettings()
    {
        try
        {
            var profile = new AiProfile(SelectedAiProvider, AiModel.Trim(), AiEndpoint.Trim(), AiKeyVariable.Trim());
            AiSettingsStore.Validate(profile);
            _aiSettings.SaveProfile(profile);
            _aiProfiles[SelectedAiProvider] = profile;
            Status = "Non-secret AI profile saved locally. No API key, token or page content was copied into settings.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Status = "AI settings were not saved. Check the endpoint, model, environment variable NAME and local file permissions.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void ImportAiSettings()
    {
        var dialog = new OpenFileDialog { Title = "Import non-secret AI profile fields from an approved appsettings.json", Filter = "JSON configuration (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var profiles = _aiSettings.ImportReference(dialog.FileName);
            _aiSettings.Save(profiles);
            _aiProfiles.Clear();
            foreach (var profile in profiles) _aiProfiles[profile.Provider] = profile;
            ApplySavedAiProfile();
            Status = "Imported provider/model/endpoint/environment-variable NAMES only. Secret values, logging and unrelated configuration were not copied. Review each destination before approving AI use.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            Status = "AI configuration import failed. Choose an approved JSON file with a valid AI section. No secret values were copied.";
        }
    }
}
