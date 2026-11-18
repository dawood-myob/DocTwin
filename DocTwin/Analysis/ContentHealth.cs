using System.Globalization;
using DocTwin.Confluence;

namespace DocTwin.Analysis;

public sealed record PageHealthRow(string Title, Uri Url, string Path, string Status, int Words, string Updated,
    string DuplicateGroup, string Advice, int Severity)
{
    public string PageId { get; init; } = string.Empty;
    public bool NeedsAttention => Severity > 0;
}

public sealed record DuplicateGroupSummary(int Number, IReadOnlyList<string> Titles, int Pages, int RedundantWords);

public sealed class ContentHealthReport
{
    public const int ThinWordLimit = 50;
    public const int StaleMonths = 18;
    public const int ReadingWordsPerMinute = 200;

    public IReadOnlyList<PageHealthRow> Rows { get; init; } = [];
    public IReadOnlyList<DuplicateGroupSummary> Groups { get; init; } = [];
    public int PagesAssessed { get; init; }
    public int EmptyPages { get; init; }
    public int ThinPages { get; init; }
    public int StalePages { get; init; }
    public int DuplicatePages { get; init; }
    public int RedundantPages { get; init; }
    public int RedundantWords { get; init; }
    public int HealthyPages { get; init; }

    public string Summary => PagesAssessed == 0
        ? "No page content was analysed yet. Run discovery to see the content health summary."
        : $"{PagesAssessed} pages assessed in the source tree. {HealthyPages} look healthy; {PagesAssessed - HealthyPages} have at least one suggestion.\n" +
          $"Empty: {EmptyPages} | Thin (under {ThinWordLimit} words): {ThinPages} | Not updated for {StaleMonths}+ months: {StalePages} | In duplicate groups: {DuplicatePages}.\n" +
          $"Duplicate groups: {Groups.Count}. If one page were kept per group, {RedundantPages} pages and about {RedundantWords:N0} words (roughly {Math.Max(RedundantWords / ReadingWordsPerMinute, RedundantWords > 0 ? 1 : 0)} minutes of reading per pass) would be redundant. " +
          "These are suggestions for a person to review, not facts or instructions; DocTwin never changes Confluence.";
}

public static class ContentHealthAnalyzer
{
    public static ContentHealthReport Analyse(DiscoveryResults results, DateTimeOffset? now = null)
    {
        var today = now ?? DateTimeOffset.UtcNow;
        var parentIds = results.HierarchyNodes.Where(x => x.ParentId != null).Select(x => x.ParentId!).ToHashSet(StringComparer.Ordinal);
        var groupOf = BuildGroups(results, out var groups);
        var rows = new List<PageHealthRow>();

        foreach (var empty in results.EmptySourcePages)
        {
            bool container = parentIds.Contains(empty.Page.Id);
            rows.Add(new PageHealthRow(empty.Page.Title, empty.Page.Url, empty.Path, "Empty", 0, empty.Page.Updated, string.Empty,
                container
                    ? "No readable text but it has child pages, so it may be an index/container page. Confirm that is intended; otherwise add a short overview."
                    : "No readable text. Ask the owner whether it is a placeholder or holds content only in attachments or macros; review it for removal or completion.", 3) { PageId = empty.Page.Id });
        }

        foreach (var discovered in results.SourcePages)
        {
            var page = discovered.Page;
            int words = CountWords(page.Content);
            var issues = new List<string>();
            var advice = new List<string>();
            int severity = 0;
            if (words < ContentHealthReport.ThinWordLimit)
            {
                issues.Add("Thin");
                advice.Add($"Only {words} words. Add purpose, owner and steps, or merge it into a related page.");
                severity = Math.Max(severity, 2);
            }
            if (IsStale(page.Updated, today, out int months))
            {
                issues.Add("Stale");
                advice.Add($"Last updated about {months} months ago. Ask the owner to confirm it is still accurate, or review it for archiving.");
                severity = Math.Max(severity, 1);
            }
            string group = string.Empty;
            if (groupOf.TryGetValue(page.Id, out int number))
            {
                var summary = groups[number - 1];
                group = $"Group {number} ({summary.Pages} pages)";
                issues.Add("Duplicate group");
                advice.Add("Appears in a group of exact or near-duplicate pages. Confirm the canonical page, ask an owner to reconcile, or review the copies for archiving.");
                severity = Math.Max(severity, 2);
            }
            rows.Add(new PageHealthRow(page.Title, page.Url, discovered.Path, issues.Count == 0 ? "Healthy" : string.Join(" + ", issues), words, page.Updated, group,
                issues.Count == 0 ? "No issues detected by the automated checks. This is not a quality guarantee." : string.Join(" ", advice), severity) { PageId = page.Id });
        }

        int redundantPages = groups.Sum(x => x.Pages - 1);
        return new ContentHealthReport
        {
            Rows = rows.OrderByDescending(x => x.Severity).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToArray(),
            Groups = groups,
            PagesAssessed = rows.Count,
            EmptyPages = rows.Count(x => x.Status == "Empty"),
            ThinPages = rows.Count(x => x.Status.Contains("Thin", StringComparison.Ordinal)),
            StalePages = rows.Count(x => x.Status.Contains("Stale", StringComparison.Ordinal)),
            DuplicatePages = groupOf.Count,
            RedundantPages = redundantPages,
            RedundantWords = groups.Sum(x => x.RedundantWords),
            HealthyPages = rows.Count(x => x.Status == "Healthy")
        };
    }

    public static int CountWords(string text)
    {
        int count = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            bool letter = char.IsLetterOrDigit(c);
            if (letter && !inWord) count++;
            inWord = letter;
        }
        return count;
    }

    private static bool IsStale(string updated, DateTimeOffset now, out int months)
    {
        months = 0;
        if (!DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return false;
        months = (int)((now - date).TotalDays / 30.44);
        return months >= ContentHealthReport.StaleMonths;
    }

    private static bool IsDuplicate(DuplicateFinding finding) =>
        finding.Relationship.StartsWith("Exact", StringComparison.Ordinal) ||
        finding.Content?.Verdict is ContentVerdict.Identical or ContentVerdict.Duplicate or ContentVerdict.NearDuplicate;

    // Union-find over pages linked by exact or near-duplicate findings; page IDs are unique across spaces.
    private static Dictionary<string, int> BuildGroups(DiscoveryResults results, out List<DuplicateGroupSummary> groups)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var pages = new Dictionary<string, ConfluencePage>(StringComparer.Ordinal);
        string Find(string id)
        {
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]];
                id = parent[id];
            }
            return id;
        }
        foreach (var finding in results.Findings.Where(IsDuplicate))
        {
            foreach (var page in new[] { finding.Source, finding.Candidate })
            {
                pages[page.Id] = page;
                parent.TryAdd(page.Id, page.Id);
            }
            parent[Find(finding.Source.Id)] = Find(finding.Candidate.Id);
        }

        groups = [];
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var members in parent.Keys.GroupBy(Find).Where(x => x.Count() > 1).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            var group = members.Select(id => pages[id]).ToArray();
            var words = group.Select(x => CountWords(x.Content)).ToArray();
            int number = groups.Count + 1;
            groups.Add(new DuplicateGroupSummary(number, group.Select(x => x.Title).ToArray(), group.Length, words.Sum() - words.Max()));
            foreach (var page in group)
                map[page.Id] = number;
        }
        return map;
    }
}
