using System.IO;
using System.Text.Json;
using DocTwin.Analysis;

namespace DocTwin.Persistence;

// Non-secret scan setup remembered between runs. The API token is never stored here.
public sealed record ScanSettings(
    string RootUrl,
    string SearchSpaceKeys,
    bool AllAccessibleSpaces,
    string SourceLimit,
    string ExternalLimit,
    string LexicalThreshold,
    DiscoveryMode Mode,
    bool Authorised,
    bool SaveConsent = true,
    bool AiThin = true,
    bool AiStale = true,
    bool AiDuplicate = true,
    bool AiHealthy = true);

public sealed class ScanSettingsStore(string? path = null)
{
    private const int MaximumBytes = 16384;
    private const int MaximumFieldLength = 2000;
    // DOCTWIN_SETTINGS_DIR lets automated checks keep their data away from the user's real settings.
    private readonly string _path = Path.GetFullPath(path ?? Path.Combine(
        Environment.GetEnvironmentVariable("DOCTWIN_SETTINGS_DIR") is { Length: > 0 } dir ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocTwin"),
        "scan-settings.json"));
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ScanSettings? Read()
    {
        if (!File.Exists(_path)) return null;
        var settings = LocalJsonFile.Read<ScanSettings>(_path, MaximumBytes) ?? throw new JsonException("Scan settings are invalid.");
        Validate(settings);
        return settings;
    }

    public void Save(ScanSettings settings)
    {
        Validate(settings);
        using var lease = LocalJsonFile.AcquireWriteLock(_path);
        LocalJsonFile.Write(_path, JsonSerializer.Serialize(settings, Options), MaximumBytes);
    }

    private static void Validate(ScanSettings settings)
    {
        string?[] fields = [settings.RootUrl, settings.SearchSpaceKeys, settings.SourceLimit, settings.ExternalLimit, settings.LexicalThreshold];
        if (fields.Any(x => x == null || x.Length > MaximumFieldLength || x.Any(char.IsControl)) || !Enum.IsDefined(settings.Mode))
            throw new InvalidDataException("Scan settings are invalid. Nothing was changed.");
    }
}
