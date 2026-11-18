using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocTwin.Analysis;
using DocTwin.Networking;
using GitHub.Copilot;

namespace DocTwin.AI;

public sealed class ProviderSwitchingAiComparison(HttpClient httpClient) : IAiComparisonProvider, IAiQualityAdvisor
{
    public async Task<AiComparisonResult> CompareAsync(AiComparisonRequest request, CancellationToken cancellationToken)
    {
        AiSettingsStore.Validate(new AiProfile(request.Provider, request.Model, request.Endpoint.OriginalString,
            request.ApiKeyEnvironmentVariable));
        string input = AiComparisonContract.BuildInput(request.Source, request.Candidate,
            request.SourceRootId, request.SourceRootTitle, request.SourcePath);
        string json = request.Provider switch
        {
            AiProvider.OpenAICompatible => await CompleteHttpAsync(request.Connection, input, false, Comparison, cancellationToken),
            AiProvider.Claude => await CompleteHttpAsync(request.Connection, input, true, Comparison, cancellationToken),
            AiProvider.GitHubCopilot => await CompleteCopilotAsync(request.Connection, input, Comparison, cancellationToken),
            _ => throw new InvalidDataException("Unsupported AI provider.")
        };
        return AiComparisonContract.Parse(json, request.Source, request.Candidate);
    }

    private sealed record AiCall(string SystemPrompt, JsonElement Schema, string SchemaName);
    private static AiCall Comparison => new(AiComparisonContract.SystemPrompt, AiComparisonContract.Schema, "doctwin_comparison");
    private static AiCall Quality => new(AiQualityContract.SystemPrompt, AiQualityContract.Schema, AiQualityContract.SchemaName);

    public async Task<AiQualityResult> AssessAsync(AiQualityRequest request, CancellationToken cancellationToken)
    {
        var connection = request.Connection;
        AiSettingsStore.Validate(new AiProfile(connection.Provider, connection.Model, connection.Endpoint.OriginalString, connection.ApiKeyEnvironmentVariable));
        string input = AiQualityContract.BuildInput(request.Page, request.Path);
        string json = connection.Provider switch
        {
            AiProvider.OpenAICompatible => await CompleteHttpAsync(connection, input, false, Quality, cancellationToken),
            AiProvider.Claude => await CompleteHttpAsync(connection, input, true, Quality, cancellationToken),
            AiProvider.GitHubCopilot => await CompleteCopilotAsync(connection, input, Quality, cancellationToken),
            _ => throw new InvalidDataException("Unsupported AI provider.")
        };
        return AiQualityContract.Parse(json, request.Page);
    }

    private async Task<string> CompleteHttpAsync(AiConnection request, string input, bool claude, AiCall call,
        CancellationToken cancellationToken)
    {
        // Backstop for every provider: the final payload is checked again before any key is read or request is made.
        SensitiveContentGuard.EnsureClean(input);
        var endpoint = request.Endpoint;
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 ||
            (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)))
            throw new InvalidDataException("AI endpoint must use HTTPS. HTTP is allowed only on loopback for a local provider; embedded credentials and fragments are not allowed.");
        if (claude && endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Claude requires an HTTPS Messages API endpoint.");
        if (string.IsNullOrWhiteSpace(request.ApiKeyEnvironmentVariable))
            throw new InvalidDataException("Enter the name of the environment variable containing the AI API key, not the key itself.");
        var key = AiCredentials.Read(request.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidDataException($"Set the {request.ApiKeyEnvironmentVariable} environment variable before using this provider. A chat subscription is not an API key. No page content was sent.");

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (claude)
        {
            message.Headers.Add("x-api-key", key);
            message.Headers.Add("anthropic-version", "2023-06-01");
            message.Content = JsonContent.Create(new
            {
                model = request.Model,
                max_tokens = 8192,
                system = call.SystemPrompt,
                messages = new[] { new { role = "user", content = input } },
                output_config = new { format = new { type = "json_schema", schema = call.Schema } }
            });
        }
        else
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            message.Content = JsonContent.Create(new
            {
                model = request.Model,
                max_completion_tokens = 8192,
                response_format = new { type = "json_schema", json_schema = new { name = call.SchemaName, strict = true, schema = call.Schema } },
                messages = new[]
                {
                    new { role = "system", content = call.SystemPrompt },
                    new { role = "user", content = input }
                }
            });
        }

        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string guidance = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Check the API key and its environment variable; a chat subscription is not API access.",
                System.Net.HttpStatusCode.Forbidden => "Check organisation policy, API permissions and model access.",
                System.Net.HttpStatusCode.NotFound => "Check the API endpoint path and model ID.",
                System.Net.HttpStatusCode.TooManyRequests => "Check rate limits, quota and billing; wait before retrying.",
                System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.RequestEntityTooLarge => "Check model structured-output support and input/token limits; this request was not truncated.",
                _ => "Check provider availability, endpoint and model configuration."
            };
            throw new InvalidDataException($"AI provider returned HTTP {(int)response.StatusCode}. {guidance} No AI findings were accepted. The request was not automatically retried.");
        }
        var bytes = await BoundedResponseBody.ReadAsync(response.Content, 1024 * 1024, httpClient.Timeout, cancellationToken);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("AI provider returned an unexpected response structure.");
        if (claude)
        {
            if (!root.TryGetProperty("stop_reason", out var stop) || stop.ValueKind != JsonValueKind.String || stop.GetString() != "end_turn" ||
                !root.TryGetProperty("content", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Claude returned an incomplete, refused or unsupported response. No AI findings were accepted.");
            var texts = new List<string>();
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Claude returned an invalid content block.");
                if (type.GetString() == "text" && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    texts.Add(text.GetString()!);
                else
                    throw new InvalidDataException("Claude returned a non-text response. No AI findings were accepted.");
            }
            if (texts.Count == 0)
                throw new InvalidDataException("Claude returned no comparison text.");
            return string.Concat(texts);
        }

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
            choices[0].ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("AI provider returned no comparison message.");
        var choice = choices[0];
        if (!choice.TryGetProperty("finish_reason", out var finish) || finish.ValueKind != JsonValueKind.String || finish.GetString() != "stop" ||
            !choice.TryGetProperty("message", out var result) || result.ValueKind != JsonValueKind.Object ||
            (result.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null) ||
            !result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(content.GetString()))
            throw new InvalidDataException("AI response was incomplete, refused or unsupported. No AI findings were accepted.");
        return content.GetString()!;
    }

    private static async Task<string> CompleteCopilotAsync(AiConnection request, string input, AiCall call,
        CancellationToken cancellationToken)
    {
        SensitiveContentGuard.EnsureClean(input);
        var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.OrdinalIgnoreCase);
        environment.Remove("COPILOT_SDK_AUTH_TOKEN");
        environment.Remove("COPILOT_RUNTIME_PROCESS_FILE_LOGGING");
        environment.Remove(AiCredentials.CopilotVariable);
        environment["COPILOT_GITHUB_TELEMETRY_DISABLED"] = "true";
        // COPILOT_GITHUB_TOKEN is used when present; otherwise the user's 'copilot login' session applies.
        string? gitHubToken = AiCredentials.Read(AiCredentials.CopilotVariable);
        await using var client = new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            BaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot"),
            GitHubToken = gitHubToken,
            UseLoggedInUser = gitHubToken == null,
            LogLevel = CopilotLogLevel.None,
            Environment = environment,
            WorkingDirectory = AppContext.BaseDirectory
        });
        try
        {
            await client.StartAsync(cancellationToken);
            var auth = await client.GetAuthStatusAsync(cancellationToken);
            if (!auth.IsAuthenticated)
                throw new InvalidDataException("GitHub Copilot SDK is not signed in. Set the COPILOT_GITHUB_TOKEN environment variable (fine-grained token with the 'Copilot Requests' permission) or run 'copilot login' in a terminal, and check your Copilot subscription. IDE and Microsoft 365 Copilot sign-ins are not assumed. No page content was sent.");
            await using var session = await client.CreateSessionAsync(new SessionConfig
            {
                Model = request.Model,
                AvailableTools = [],
                SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = call.SystemPrompt },
                EnableSessionStore = false,
                EnableSessionTelemetry = false,
                InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
                Memory = new MemoryConfiguration { Enabled = false }
            }, cancellationToken);
            try
            {
#pragma warning disable GHCP001 // Structured output is experimental in the pinned 1.0.28 SDK.
                var result = await session.SendAndWaitAsync(new MessageOptions
                {
                    Prompt = input,
                    ResponseSchema = call.Schema
                }, TimeSpan.FromMinutes(2), cancellationToken);
#pragma warning restore GHCP001
                return !string.IsNullOrWhiteSpace(result?.Data.Content) ? result.Data.Content :
                    throw new InvalidDataException("GitHub Copilot returned no comparison.");
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await session.AbortAsync(cleanup.Token);
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            throw new InvalidDataException("GitHub Copilot runtime could not complete the request. Check CLI installation, 'copilot login', subscription and model access. No AI findings were accepted.", ex);
        }
    }
}
