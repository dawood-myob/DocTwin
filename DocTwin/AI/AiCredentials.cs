namespace DocTwin.AI;

/// <summary>
/// Resolves AI credentials from environment variables only. Process scope wins; on Windows the
/// user-scope value is also read so a key set after the app (or Visual Studio) started is still found.
/// Values are never logged, displayed or persisted by DocTwin.
/// </summary>
public static class AiCredentials
{
    public const string OpenAiVariable = "OPENAI_API_KEY";
    public const string ClaudeVariable = "ANTHROPIC_API_KEY";
    public const string CopilotVariable = "COPILOT_GITHUB_TOKEN";

    public static string? Read(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) && OperatingSystem.IsWindows())
            value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static bool IsSet(string? name) => Read(name) != null;

    public static string Describe(AiProvider provider, string? variableName) => provider == AiProvider.GitHubCopilot
        ? IsSet(CopilotVariable)
            ? $"{CopilotVariable} is set and will be used to authenticate Copilot (value hidden)."
            : $"{CopilotVariable} is not set. Copilot will fall back to 'copilot login' sign-in."
        : IsSet(variableName)
            ? $"{variableName} is set (value hidden)."
            : $"{variableName} is NOT set. Set it as a user environment variable, then choose Re-check and restart DocTwin if it still shows as missing.";
}
