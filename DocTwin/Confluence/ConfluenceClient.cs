using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;

namespace DocTwin.Confluence;

public sealed partial class ConfluenceClient(HttpClient httpClient, PageContentExtractor extractor) : IConfluenceClient
{
    private readonly ResiliencePipeline<HttpResponseMessage> _readPipeline = ConfluenceReadPipeline.Create();

    public async Task TestConnectionAsync(ConnectionProfile profile, string token, CancellationToken cancellationToken)
    {
        using var identity = await ReadAsync(profile, token, "rest/api/user/current", cancellationToken);
        if (!identity.RootElement.TryGetProperty("accountId", out var account) ||
            account.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(account.GetString()) ||
            (identity.RootElement.TryGetProperty("type", out var type) &&
             (type.ValueKind != JsonValueKind.String || type.GetString() == "anonymous")))
            throw new ConfluenceException("Confluence did not confirm an authenticated account. Check the email and token.");

        using var pages = await ReadAsync(profile, token, "api/v2/pages?limit=1", cancellationToken);
        if (!pages.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new ConfluenceException("Confluence returned an unexpected page-list response. The connection was not validated.");
    }

    public async Task<ConfluencePage> GetPageAsync(ConnectionProfile profile, string token, string pageId,
        CancellationToken cancellationToken)
    {
        if (!MyRegex().IsMatch(pageId))
            throw new ArgumentException("Invalid Confluence page ID.");
        using var json = await ReadAsync(profile, token, $"api/v2/pages/{pageId}?body-format=storage", cancellationToken);
        var root = json.RootElement;
        if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != pageId ||
            !root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(title.GetString()) ||
            !root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("storage", out var storage) || storage.ValueKind != JsonValueKind.Object ||
            !storage.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
            throw new ConfluenceException("Confluence returned a page without the expected identity, title or storage body. No comparison was produced.");

        var extracted = await Task.Run(() => extractor.Extract(value.GetString()!), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        int version = 0;
        string updated = "Not supplied";
        if (root.TryGetProperty("version", out var pageVersion) && pageVersion.ValueKind == JsonValueKind.Object)
        {
            if (pageVersion.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out int parsed))
                version = parsed;
            if (pageVersion.TryGetProperty("createdAt", out var timestamp) && timestamp.ValueKind == JsonValueKind.String)
                updated = timestamp.GetString() ?? updated;
        }
        return new ConfluencePage(pageId, title.GetString()!,
            root.TryGetProperty("spaceId", out var space) && space.ValueKind == JsonValueKind.String ? space.GetString() ?? "Not supplied" : "Not supplied",
            version, updated, new Uri(profile.Site, $"wiki/pages/viewpage.action?pageId={pageId}"),
            extracted.Text, extracted.Limitations)
        {
            ParentId = OptionalString(root, "parentId"),
            ParentType = OptionalString(root, "parentType"),
            OwnerId = OptionalString(root, "ownerId"),
            ExtractionStatus = extracted.Status
        };
    }

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    internal async Task<JsonDocument> ReadAsync(ConnectionProfile profile, string token, string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("Enter a valid API token without whitespace.");

        profile = ConnectionProfile.Create(profile.Site.AbsoluteUri, profile.Email, profile.ScopedToken, profile.CloudId);
        var uri = new Uri(profile.ApiBase, path);
        try
        {
            using var response = await _readPipeline.ExecuteAsync(async cancellation =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{profile.Email}:{token}")));
                return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            }, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength > 10 * 1024 * 1024)
                    throw new ConfluenceException("The page response exceeds the 10 MB prototype limit.");
                await response.Content.LoadIntoBufferAsync(10 * 1024 * 1024, cancellationToken);
                var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    document.Dispose();
                    throw new ConfluenceException("Confluence returned an unexpected response structure. No successful result was assumed.");
                }
                return document;
            }

            if (ConfluenceReadPipeline.IsTransient(response.StatusCode) &&
                ConfluenceReadPipeline.RetryDelay(response, 0) > TimeSpan.FromSeconds(30))
                throw new ConfluenceException("Confluence requested a longer retry delay. Wait and try again later.", stopScan: true)
                { StatusCode = response.StatusCode };

            throw new ConfluenceException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Authentication failed. The email or token is invalid, expired or revoked.",
                HttpStatusCode.Forbidden => "Access denied. Check page permissions, organisation policy, and token read scopes (read:page:confluence and read:user:confluence).",
                HttpStatusCode.NotFound => "The page or API endpoint was not found or is not visible to your account. Check the site, Cloud ID, token type and page URLs.",
                HttpStatusCode.TooManyRequests => "Confluence is rate limiting requests. Wait and try again later.",
                >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => "Confluence redirected the request. Redirects are blocked to protect your token. Check the site and token type.",
                _ => $"Confluence returned HTTP {(int)response.StatusCode}. No successful result was assumed. Try again or contact your administrator."
            }, response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests or
                HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            { StatusCode = response.StatusCode };
        }
        catch (BrokenCircuitException)
        {
            throw new ConfluenceException("Confluence reads are temporarily paused after repeated transient failures. Wait 30 seconds, then retry. No successful result was assumed.", stopScan: true);
        }
        catch (RateLimiterRejectedException)
        {
            throw new ConfluenceException("DocTwin's Confluence read concurrency limit was reached. Wait for the active reads to finish, then retry.", stopScan: true);
        }
    }

    [GeneratedRegex(@"\A[0-9]+\z")]
    private static partial Regex MyRegex();
}
