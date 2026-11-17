using System.Net.Mail;
using System.Text.RegularExpressions;

namespace DocTwin.Confluence;

public sealed partial record ConnectionProfile(Uri Site, string Email, bool ScopedToken, string CloudId)
{
    public Uri ApiBase => ScopedToken
        ? new Uri($"https://api.atlassian.com/ex/confluence/{CloudId}/wiki/")
        : new Uri(Site, "wiki/");

    public static ConnectionProfile Create(string site, string email, bool scopedToken, string cloudId)
    {
        if (!Uri.TryCreate(site.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !MyRegex().IsMatch(uri.Host) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.AbsolutePath != "/" && uri.AbsolutePath.TrimEnd('/') != "/wiki"))
            throw new ArgumentException("Enter your Confluence Cloud site, such as https://example.atlassian.net (not a page URL). Custom domains are not supported yet.");

        email = email.Trim();
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email || email.Contains(':'))
            throw new ArgumentException("Enter a valid Atlassian account email address.");

        cloudId = cloudId.Trim();
        if (scopedToken && !Guid.TryParseExact(cloudId, "D", out _))
            throw new ArgumentException("Scoped tokens require your site's Cloud ID (a UUID). Ask your Atlassian administrator for it.");

        return new ConnectionProfile(new Uri(uri.GetLeftPart(UriPartial.Authority) + "/"), email,
            scopedToken, scopedToken ? cloudId : string.Empty);
    }

    public string ResolvePageId(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != Site.Host || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0)
            throw new ArgumentException("Both page URLs must belong to the configured HTTPS Confluence site.");

        var match = PagePathRegex().Match(uri.AbsolutePath);
        if (match.Success)
            return match.Groups[1].Value;

        if (uri.AbsolutePath == "/wiki/pages/viewpage.action")
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0] == "pageId" && PageIdRegex().IsMatch(parts[1]))
                    return parts[1];
            }
        }

        throw new ArgumentException("Use a full Confluence page URL containing /wiki/spaces/.../pages/12345, or /wiki/pages/viewpage.action?pageId=12345. Short links are not supported yet.");
    }

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]*\.atlassian\.net\z", RegexOptions.IgnoreCase, "en-AU")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"\A/wiki/spaces/[^/]+/pages/([0-9]+)(?:/|\z)")]
    private static partial Regex PagePathRegex();

    [GeneratedRegex(@"\A[0-9]+\z")]
    private static partial Regex PageIdRegex();
}

public sealed class ConfluenceException(string message, bool stopScan = false) : Exception(message)
{
    public bool StopScan { get; } = stopScan;
    public System.Net.HttpStatusCode? StatusCode { get; init; }
}

public sealed record ConfluencePage(string Id, string Title, string SpaceId, int Version,
    string Updated, Uri Url, string Content, IReadOnlyList<string> Limitations)
{
    public string? ParentId { get; init; }
    public string? ParentType { get; init; }
    public string? OwnerId { get; init; }
    public ExtractionStatus ExtractionStatus { get; init; }
}

public interface IConfluenceClient
{
    Task TestConnectionAsync(ConnectionProfile profile, string token, CancellationToken cancellationToken);
    Task<ConfluencePage> GetPageAsync(ConnectionProfile profile, string token, string pageId,
        CancellationToken cancellationToken);
}
