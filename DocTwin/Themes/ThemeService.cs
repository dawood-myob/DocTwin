using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using DocTwin.Persistence;

namespace DocTwin.Themes;

public sealed class ThemeService : INotifyPropertyChanged
{
    public static ThemeService Current { get; } = new();
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocTwin", "appearance.json");
    private string _theme = "Dark";
    private string _status = "Switch appearance. Only the theme preference is saved; no credentials or content.";
    public string Theme => _theme;
    public string SwitchLabel => _theme == "Dark" ? "Light mode" : "Dark mode";
    public string Status => _status;
    public ICommand ToggleCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    private ThemeService() => ToggleCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(Toggle);

    public void Initialize()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            var saved = LocalJsonFile.Read<AppearanceSettings>(SettingsPath, 1024);
            if (saved?.Theme is not ("Dark" or "Light")) throw new JsonException("Unsupported appearance preference.");
            Apply(saved.Theme);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _status = "The saved appearance preference could not be loaded. Dark mode is being used; toggle the theme to save a new preference.";
            MessageBox.Show(_status, "DocTwin appearance", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void Apply(string theme)
    {
        if (theme is not ("Dark" or "Light")) throw new ArgumentException("Theme must be Dark or Light.", nameof(theme));
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri("/DocTwin;component/Themes/" + (theme == "Light" ? "LightTheme.xaml" : "EmeraldPalette.xaml"), UriKind.Relative) };
        dictionaries[0] = palette;
        _theme = theme;
        Changed(nameof(Theme));
        Changed(nameof(SwitchLabel));
    }

    private void Toggle()
    {
        Apply(_theme == "Dark" ? "Light" : "Dark");
        try
        {
            using var lease = LocalJsonFile.AcquireWriteLock(SettingsPath);
            LocalJsonFile.Write(SettingsPath, JsonSerializer.Serialize(new AppearanceSettings { Theme = _theme }), 1024);
            _status = "Appearance saved for this Windows account.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status = "Theme changed for this session, but Windows could not save the preference. Check access or retry after another instance finishes saving.";
            MessageBox.Show(_status, "DocTwin appearance", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Changed(nameof(Status));
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private sealed class AppearanceSettings { public string Theme { get; set; } = "Dark"; }
}
