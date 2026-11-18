using System.IO;
using DocTwin.Analysis;
using DocTwin.Confluence;

namespace DocTwin.AI;

public sealed record PlannedPage(ConfluencePage Page, string Path, int Priority);

public sealed record SkippedPage(string Title, string Reason);

/// <summary>Which content-health statuses the user wants reviewed. A page qualifies if any of its statuses is ticked.</summary>
public sealed record AiQualityScope(bool Thin = true, bool Stale = true, bool Duplicate = true, bool Healthy = true)
{
    public bool IsEmpty => !Thin && !Stale && !Duplicate && !Healthy;

    public bool Includes(string? status) => status == null || status == "Healthy" ? Healthy :
        (Thin && status.Contains("Thin", StringComparison.Ordinal)) ||
        (Stale && status.Contains("Stale", StringComparison.Ordinal)) ||
        (Duplicate && status.Contains("Duplicate group", StringComparison.Ordinal));
}

/// <summary>What would be sent if the user consents: shown in full before any content leaves the machine.</summary>
public sealed class AiQualityPlan
{
    public const int DefaultMaxPages = 50;
    public const int DefaultMaxTotalCharacters = 600000;

    public IReadOnlyList<PlannedPage> Pages { get; init; } = [];
    public IReadOnlyList<SkippedPage> Skipped { get; init; } = [];
    public int ExcludedByScope { get; init; }
    public bool ScopeEmpty { get; init; }
    public int TotalCharacters => Pages.Sum(x => x.Page.Content.Length);

    public static AiQualityPlan Create(IReadOnlyList<DiscoveredPage> pages, ContentHealthReport? health,
        int maxPages = DefaultMaxPages, int maxTotalCharacters = DefaultMaxTotalCharacters, AiQualityScope? scope = null)
    {
        scope ??= new AiQualityScope();
        var rows = health?.Rows.Where(x => x.PageId.Length > 0).ToDictionary(x => x.PageId, StringComparer.Ordinal)
            ?? new Dictionary<string, PageHealthRow>();
        var severity = rows.ToDictionary(x => x.Key, x => x.Value.Severity, StringComparer.Ordinal);
        var planned = new List<PlannedPage>();
        var skipped = new List<SkippedPage>();
        long total = 0;
        int excluded = 0;
        // Pages with automated flags first; they are the likeliest to benefit from review.
        foreach (var item in pages.OrderByDescending(x => severity.GetValueOrDefault(x.Page.Id)).ThenBy(x => x.Page.Title, StringComparer.OrdinalIgnoreCase))
        {
            var page = item.Page;
            if (!scope.Includes(rows.TryGetValue(page.Id, out var row) ? row.Status : null))
            {
                excluded++;
                continue;
            }
            int length = page.Content.Length + item.Path.Length;
            if (page.ExtractionStatus != ExtractionStatus.Complete || string.IsNullOrWhiteSpace(page.Content))
                skipped.Add(new SkippedPage(page.Title, "no usable text"));
            else if (length > AiQualityContract.MaximumPageCharacters)
                skipped.Add(new SkippedPage(page.Title, $"too long for one request ({length:N0} characters; nothing is truncated)"));
            else if (planned.Count >= maxPages)
                skipped.Add(new SkippedPage(page.Title, $"over the {maxPages}-page limit for one review"));
            else if (total + length > maxTotalCharacters)
                skipped.Add(new SkippedPage(page.Title, $"over the {maxTotalCharacters:N0}-character total limit for one review"));
            else
            {
                planned.Add(new PlannedPage(page, item.Path, severity.GetValueOrDefault(page.Id)));
                total += length;
            }
        }
        return new AiQualityPlan { Pages = planned, Skipped = skipped, ExcludedByScope = excluded, ScopeEmpty = scope.IsEmpty };
    }

    public string Describe(AiConnection connection) => Pages.Count == 0
        ? ScopeEmpty ? "No page types are ticked. Tick at least one of Thin, Stale, In a duplicate group or Healthy to choose which pages the AI reviews."
          : ExcludedByScope > 0 ? $"No pages match the ticked page types ({ExcludedByScope} pages are outside your selection). Tick more page types to include them."
          : "No pages are available for AI quality review. Run discovery first; pages without usable text are never sent."
        : $"Plan: send {Pages.Count} pages ({TotalCharacters:N0} characters of page text, titles and hierarchy paths; no tokens or owner IDs) in {Pages.Count} separate requests to {connection.Provider} " +
          $"model {connection.Model}" + (connection.Provider == AiProvider.GitHubCopilot ? " via GitHub Copilot." : $" at {connection.Endpoint.Host}.") +
          (Skipped.Count == 0 ? string.Empty : $" {Skipped.Count} pages will not be sent: " + string.Join("; ", Skipped.Take(5).Select(x => $"{x.Title} ({x.Reason})")) + (Skipped.Count > 5 ? $"; and {Skipped.Count - 5} more." : ".")) +
          (ExcludedByScope == 0 ? string.Empty : $" {ExcludedByScope} pages are outside your page-type selection and will not be sent.");
}

public sealed record AiPageQuality(string PageId, string Title, Uri Url, string Path, string Rating, string Summary,
    string Issues, string SuggestedAction, string Uncertainties, string Error)
{
    public int RatingRank => Rating switch { "Poor" => 0, "NeedsRefinement" => 1, "Uncertain" => 2, "Good" => 3, _ => 4 };
    public bool Assessed => Error.Length == 0;
}

public sealed class AiQualityOutcome
{
    public List<AiPageQuality> Results { get; } = [];
    public bool StoppedEarly { get; set; }
    public string StopReason { get; set; } = string.Empty;

    public string Summary()
    {
        int assessed = Results.Count(x => x.Assessed);
        int Count(string rating) => Results.Count(x => x.Rating == rating);
        var categories = Results.SelectMany(x => x.Issues.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Split(':')[0].Trim()).Where(x => x.Length > 0)
            .GroupBy(x => x).OrderByDescending(x => x.Count()).Take(3).Select(x => $"{x.Key} ({x.Count()})");
        return $"AI reviewed {assessed} of {Results.Count} pages: Good {Count("Good")}, Needs refinement {Count("NeedsRefinement")}, Poor {Count("Poor")}, Uncertain {Count("Uncertain")}; not accepted {Results.Count - assessed}." +
            (categories.Any() ? " Most common issues: " + string.Join(", ", categories) + "." : string.Empty) +
            (StoppedEarly ? " The review stopped early: " + StopReason : string.Empty) +
            " Quotations were checked against the page text; explanations and ratings are advisory and need human review.";
    }
}

public static class AiTreeQualityReview
{
    public const int MaximumConsecutiveFailures = 3;

    public static async Task<AiQualityOutcome> RunAsync(IAiQualityAdvisor advisor, AiConnection connection, AiQualityPlan plan,
        IProgress<AiPageQuality>? pageProgress, IProgress<string>? statusProgress, CancellationToken cancellationToken)
    {
        var outcome = new AiQualityOutcome();
        int consecutiveFailures = 0;
        for (int i = 0; i < plan.Pages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var planned = plan.Pages[i];
            statusProgress?.Report($"AI quality review: page {i + 1} of {plan.Pages.Count} ({planned.Page.Title}). Requests are not retried automatically.");
            AiPageQuality row;
            try
            {
                var result = await advisor.AssessAsync(new AiQualityRequest(connection, planned.Page, planned.Path), cancellationToken);
                consecutiveFailures = 0;
                row = new AiPageQuality(planned.Page.Id, planned.Page.Title, planned.Page.Url, planned.Path, result.Rating, result.Summary,
                    string.Join("\n", result.Issues.Select(x => $"{x.Category}: {x.Explanation} Suggestion: {x.Suggestion}" + (x.Quote.Length == 0 ? string.Empty : $" (\"{x.Quote}\")"))),
                    result.SuggestedAction, string.Join("; ", result.Uncertainties), string.Empty);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or TimeoutException or UnauthorizedAccessException)
            {
                consecutiveFailures++;
                row = new AiPageQuality(planned.Page.Id, planned.Page.Title, planned.Page.Url, planned.Path, "Not accepted", string.Empty, string.Empty, string.Empty, string.Empty,
                    ex is System.Net.Http.HttpRequestException ? "Network or TLS failure." : ex.Message);
            }
            outcome.Results.Add(row);
            pageProgress?.Report(row);
            if (consecutiveFailures >= MaximumConsecutiveFailures)
            {
                outcome.StoppedEarly = true;
                outcome.StopReason = $"{MaximumConsecutiveFailures} requests in a row were not accepted, so the remaining pages were not sent. Check the provider settings, key and model, then run again.";
                break;
            }
        }
        return outcome;
    }
}
