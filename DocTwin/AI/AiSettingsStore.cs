using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocTwin.Persistence;

namespace DocTwin.AI;

public sealed record AiProfile(AiProvider Provider, string Model, string Endpoint, string ApiKeyEnvironmentVariable);

public sealed partial class AiSettingsStore(string? path = null)
{
    private readonly string _path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocTwin", "ai-profiles.json"));
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public IReadOnlyList<AiProfile> Read()
    {
        if (!File.Exists(_path)) return [];
        var profiles = LocalJsonFile.Read<List<AiProfile>>(_path, 65536) ?? throw new JsonException("AI profile settings are invalid.");
        foreach (var profile in profiles) Validate(profile);
        if (profiles.Select(x => x.Provider).Distinct().Count() != profiles.Count) throw new InvalidDataException("AI settings contain duplicate provider profiles.");
        return profiles;
    }
    public void Save(IReadOnlyList<AiProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        foreach (var profile in profiles) Validate(profile);
        if (profiles.Select(x => x.Provider).Distinct().Count() != profiles.Count)
            throw new InvalidDataException("AI settings contain duplicate provider profiles. No configuration was saved.");
        using var lease = LocalJsonFile.AcquireWriteLock(_path);
        LocalJsonFile.Write(_path, JsonSerializer.Serialize(profiles, Options), 65536);
    }
    public void SaveProfile(AiProfile profile)
    {
        Validate(profile);
        using var lease = LocalJsonFile.AcquireWriteLock(_path);
        var profiles = Read().Where(x => x.Provider != profile.Provider).Append(profile).ToArray();
        LocalJsonFile.Write(_path, JsonSerializer.Serialize(profiles, Options), 65536);
    }

    public IReadOnlyList<AiProfile> ImportReference(string path)
    {
        var root = LocalJsonFile.Read<JsonElement>(path, 1024 * 1024);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("AI", out var ai) || ai.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Choose a configuration file with an AI section containing the provider profiles.");
        static string Read(JsonElement entry, string name) => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()) ? item.GetString()! : throw new InvalidDataException("AI profile is missing " + name + ". No configuration was saved.");
        var profiles = new List<AiProfile> { new(AiProvider.OpenAICompatible, Read(ai, "Model"), Read(ai, "Endpoint"), Read(ai, "ApiKeyEnvironmentVariable")) };
        if (ai.TryGetProperty("Claude", out var claude)) profiles.Add(new(AiProvider.Claude, Read(claude, "Model"), Read(claude, "Endpoint"), Read(claude, "ApiKeyEnvironmentVariable")));
        if (ai.TryGetProperty("Copilot", out var copilot)) profiles.Add(new(AiProvider.GitHubCopilot, Read(copilot, "Model"), "https://api.openai.com/v1/chat/completions", "UNUSED_COPILOT_CLI_AUTH"));
        foreach (var profile in profiles) Validate(profile);
        return profiles;
    }
    public static void Validate(AiProfile? profile)
    {
        if (profile == null || !Enum.IsDefined(profile.Provider) || string.IsNullOrWhiteSpace(profile.Model) ||
            profile.Model.Length > 200 || profile.Model.Any(char.IsControl))
            throw new InvalidDataException("Enter a supported provider and model ID (maximum 200 characters, without control characters).");
        if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.Query.Length != 0 ||
            (endpoint.Scheme != "https" && !(profile.Provider == AiProvider.OpenAICompatible && endpoint.Scheme == "http" && endpoint.IsLoopback)))
            throw new InvalidDataException("AI endpoints must use HTTPS (or loopback HTTP for OpenAI-compatible servers), without credentials, query strings or fragments.");
        if (string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable) || !MyRegex().IsMatch(profile.ApiKeyEnvironmentVariable))
            throw new InvalidDataException("Enter the environment variable name, not an API key. Variable names may contain letters, digits and underscores.");
    }

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]{0,99}\z")]
    private static partial Regex MyRegex();
}
