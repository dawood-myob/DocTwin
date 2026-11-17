using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocTwin.Confluence;

public sealed record ContentReference(string Id, string Title, string Type, string SpaceId,
    string? ParentId = null, string? ParentType = null, string? OwnerId = null);

public interface IConfluenceDiscovery
{
    IAsyncEnumerable<ContentReference> GetChildrenAsync(ConnectionProfile profile, string token,
        string id, string type, CancellationToken cancellationToken);
    IAsyncEnumerable<ContentReference> GetPagesAsync(ConnectionProfile profile, string token,
        IReadOnlyList<string> spaceKeys, CancellationToken cancellationToken);
}

public interface IConfluenceCandidateSearch
{
    IAsyncEnumerable<ContentReference> SearchPagesAsync(ConnectionProfile profile, string token,
        IReadOnlyList<string> titleTerms, IReadOnlyList<string> spaceKeys, CancellationToken cancellationToken);
}

public sealed partial class ConfluenceDiscovery(ConfluenceClient client) : IConfluenceDiscovery, IConfluenceCandidateSearch
{
    public async IAsyncEnumerable<ContentReference> GetChildrenAsync(ConnectionProfile profile, string token,
        string id, string type, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string resource = type switch
        {
            "page" => "pages",
            "folder" => "folders",
            "database" => "databases",
            "whiteboard" => "whiteboards",
            "embed" => "embeds",
            _ => throw new ConfluenceException("Unsupported hierarchy content type. Source-tree coverage is incomplete.")
        };
        if (!MyRegex().IsMatch(id))
            throw new ConfluenceException("Confluence returned an invalid hierarchy identifier.");
        await foreach (var item in ReadListAsync(profile, token, $"api/v2/{resource}/{id}/direct-children?limit=100", cancellationToken))
            yield return ParseReference(item, true);
    }

    public async IAsyncEnumerable<ContentReference> GetPagesAsync(ConnectionProfile profile, string token,
        IReadOnlyList<string> spaceKeys, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (spaceKeys.Count == 0)
        {
            await foreach (var item in ReadListAsync(profile, token, "api/v2/pages?limit=100&status=current", cancellationToken))
                yield return ParseReference(item, false);
            yield break;
        }
        foreach (var key in spaceKeys.Distinct(StringComparer.Ordinal))
        {
            var spaces = new List<string>();
            await foreach (var item in ReadListAsync(profile, token, "api/v2/spaces?limit=100&keys=" + Uri.EscapeDataString(key), cancellationToken))
            {
                if (GetString(item, "key") == key)
                    spaces.Add(GetId(item));
            }
            if (spaces.Count != 1)
                throw new ConfluenceException($"Space '{key}' was not uniquely resolved or is not accessible. Check its exact space key and read:space:confluence permission.");
            await foreach (var item in ReadListAsync(profile, token,
                $"api/v2/pages?limit=100&status=current&space-id={spaces[0]}", cancellationToken))
                yield return ParseReference(item, false);
        }
    }

    public async IAsyncEnumerable<ContentReference> SearchPagesAsync(ConnectionProfile profile, string token,
        IReadOnlyList<string> titleTerms, IReadOnlyList<string> spaceKeys,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (titleTerms.Count is < 1 or > 6 || titleTerms.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100))
            throw new ArgumentException("Supply one to six title search terms of at most 100 characters.", nameof(titleTerms));
        string cql = "type = page AND (" + string.Join(" AND ", titleTerms.Select(x => "title ~ " + CqlString(x))) + ")";
        if (spaceKeys.Count > 0)
            cql += " AND space IN (" + string.Join(",", spaceKeys.Distinct(StringComparer.Ordinal).Select(CqlString)) + ")";
        await foreach (var item in ReadListAsync(profile, token,
            "rest/api/search?limit=100&expand=content.space&cql=" + Uri.EscapeDataString(cql), cancellationToken))
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
                throw new ConfluenceException("Confluence title search returned invalid content metadata. Targeted coverage is incomplete.");
            if (GetString(content, "type") != "page" || GetString(content, "status") != "current")
                continue;
            string spaceId = content.TryGetProperty("space", out var space) && space.ValueKind == JsonValueKind.Object
                ? OptionalString(space, "id") ?? string.Empty : string.Empty;
            yield return new ContentReference(GetId(content), GetString(content, "title"), "page", spaceId);
        }
    }

    private static string CqlString(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private async IAsyncEnumerable<JsonElement> ReadListAsync(ConnectionProfile profile, string token,
        string initialPath, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? path = initialPath;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (path != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(path))
                throw new ConfluenceException("Confluence repeated a pagination cursor. Coverage is incomplete; the scan was stopped.");
            using var json = await client.ReadAsync(profile, token, path, cancellationToken);
            var root = json.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new ConfluenceException("Confluence returned an invalid paginated response. Coverage is incomplete.");
            foreach (var item in results.EnumerateArray())
                yield return item.Clone();
            path = null;
            if (root.TryGetProperty("_links", out var links) && links.ValueKind == JsonValueKind.Object &&
                links.TryGetProperty("next", out var next) && next.ValueKind != JsonValueKind.Null)
            {
                if (next.ValueKind != JsonValueKind.String)
                    throw new ConfluenceException("Confluence returned an invalid pagination link.");
                string? value = next.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    var url = new Uri(profile.ApiBase, value);
                    // Never follow a server-supplied host or path with credentials. Only copy its cursor.
                    if (url.Scheme != "https" || (url.Host != profile.Site.Host && url.Host != profile.ApiBase.Host))
                        throw new ConfluenceException("Confluence returned an unexpected pagination host. No credentials were forwarded.");
                    var cursor = url.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
                        .FirstOrDefault(x => x.Length == 2 && x[0] == "cursor");
                    if (cursor == null || cursor[1].Length == 0)
                        throw new ConfluenceException("Confluence returned pagination without a usable cursor. Coverage is incomplete.");
                    path = initialPath + "&cursor=" + Uri.EscapeDataString(Uri.UnescapeDataString(cursor[1]));
                }
            }
        }
    }

    private static ContentReference ParseReference(JsonElement item, bool requireType) =>
        new(GetId(item), GetString(item, "title"), requireType ? GetString(item, "type") : "page",
            requireType ? OptionalString(item, "spaceId") ?? string.Empty : GetString(item, "spaceId"),
            OptionalString(item, "parentId"), OptionalString(item, "parentType"), OptionalString(item, "ownerId"));

    private static string GetId(JsonElement item)
    {
        var id = GetString(item, "id");
        if (!MyRegex().IsMatch(id))
            throw new ConfluenceException("Confluence returned a malformed content ID.");
        return id;
    }

    private static string? OptionalString(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString() : null;

    private static string GetString(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ConfluenceException("Confluence returned incomplete content metadata. Coverage is incomplete.");
        return value.GetString()!;
    }

    [GeneratedRegex(@"\A[0-9]+\z")]
    private static partial Regex MyRegex();
}
