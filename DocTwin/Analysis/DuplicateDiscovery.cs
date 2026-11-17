using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocTwin.Confluence;

namespace DocTwin.Analysis;

public enum DiscoveryMode { Quick, Deep }

public sealed record DiscoveredPage(ConfluencePage Page, string Path);
public sealed record SourceHierarchyNode(string Id, string? ParentId, string Title, string Type, int Depth);
public sealed record DuplicateFinding(ConfluencePage Source, ConfluencePage Candidate, string SourcePath,
    string Relationship, double LexicalScore, double SmallerPageCoverage, string Evidence)
{
    public string SourceTitle => Source.Title;
    public string CandidateTitle => Candidate.Title;
    public string CandidateSpace => Candidate.SpaceId;
    public string MatchBasis { get; init; } = "Extracted-text comparison";
    public double TitleCoverage { get; init; }
    public double SourceCoverage { get; init; }
    public double CandidateOnlyContentPercent { get; init; }
    public string CoverageBasis { get; init; } = "Lexical estimate from distinct 5-word shingles; not factual completeness.";
    public string Recommendation { get; init; } = "Human review required; no content is changed.";
    public string RecommendationReason { get; init; } = "Lexical similarity does not establish authority or factual completeness.";
    public string CandidatePath { get; init; } = string.Empty;
    public string RootContext { get; init; } = string.Empty;
    public string SourceRootId { get; init; } = string.Empty;
    public string? SourceRootTitle { get; init; }
}
public sealed class DiscoveryResults
{
    public List<DiscoveredPage> SourcePages { get; } = [];
    public List<SourceHierarchyNode> HierarchyNodes { get; } = [];
    public List<DuplicateFinding> Findings { get; } = [];
    public List<string> Warnings { get; } = [];
    public int VisitedHierarchyNodes { get; set; }
    public int ExternalPages { get; set; }
    public DiscoveryMode Mode { get; set; }
    public int ExternalAttempts { get; set; }
    public int MetadataPages { get; set; }
    public int TargetedQueries { get; set; }
    public int TargetedCandidates { get; set; }
    public int SearchFailures { get; set; }
    public int FailedPages { get; set; }
    public int EmptyPages { get; set; }
    public int LimitedPages { get; set; }
    public int AccessDeniedPages { get; set; }
    public int UnavailablePages { get; set; }
    public int RequestFailedPages { get; set; }
    public string SkipSummary => $"{FailedPages} pages skipped (empty/unsupported: {EmptyPages}; size limits: {LimitedPages}; access denied: {AccessDeniedPages}; missing/not visible: {UnavailablePages}; other read errors: {RequestFailedPages})";
    public bool SourceComplete { get; set; }
    public bool SourceHierarchyComplete { get; set; }
    public bool SourceContentComplete { get; set; }
    public bool ExternalComplete { get; set; }
    public string SourceRootId { get; set; } = string.Empty;
    public string? SourceRootTitle { get; set; }
    public string RootContext { get; set; } = string.Empty;
}

public sealed partial class DuplicateDiscovery(IConfluenceClient client, IConfluenceDiscovery discovery)
{
    public const int QuickPageLimit = 100;
    public const int QuickMetadataLimit = 2000;

    public async Task ScanAsync(ConnectionProfile profile, string token, string rootId,
        IReadOnlyList<string> spaceKeys, int sourceLimit, int externalLimit, double threshold,
        DiscoveryResults results, IProgress<string> progress, CancellationToken cancellationToken,
        DiscoveryMode mode = DiscoveryMode.Deep, IProgress<DuplicateFinding>? findingProgress = null)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentException("Choose Quick or Deep discovery mode.", nameof(mode));
        results.Mode = mode;
        if (sourceLimit is < 1 or > 1000 || externalLimit is < 1 or > 5000 || !double.IsFinite(threshold) || threshold is < 0.1 or > 1)
            throw new ArgumentException("Choose 1-1,000 source nodes, 1-5,000 external pages and a 0.1-1.0 lexical threshold.");
        if (!MyRegex().IsMatch(rootId))
            throw new ArgumentException("The source root must be a numeric Confluence content ID.", nameof(rootId));
        results.SourceComplete = false;
        results.SourceHierarchyComplete = false;
        results.SourceContentComplete = false;
        results.ExternalComplete = false;
        results.SourceRootId = rootId;
        results.RootContext = $"Source root: page {rootId}; hierarchy context is descriptive, not an authority signal.";
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        results.HierarchyNodes.Clear();
        results.VisitedHierarchyNodes = 0;
        results.HierarchyNodes.Add(new SourceHierarchyNode(rootId, null, $"Root page {rootId}", "page", 0));
        var queue = new Queue<(string Id, string Type, string Path, int HierarchyIndex)>();
        queue.Enqueue((rootId, "page", "", 0));
        var scheduled = new HashSet<string>(StringComparer.Ordinal) { rootId };
        bool hierarchyFailed = false;
        bool sourceContentFailed = false;
        int nodes = 0;
        long characters = 0;
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nodes >= sourceLimit)
            {
                results.Warnings.Add("Source hierarchy limit reached. External discovery was not run: unvisited source descendants must not be reported as external duplicates. Increase the source limit and scan again.");
                return;
            }
            var node = queue.Dequeue();
            if (!sourceIds.Add(node.Id))
                continue;
            nodes++;
            results.VisitedHierarchyNodes = nodes;
            progress.Report($"Source hierarchy: {nodes} nodes visited, {results.SourcePages.Count} pages analysed, {queue.Count} known nodes pending. {results.SkipSummary}.");
            string path = node.Path;
            if (node.Type == "page")
            {
                try
                {
                    var page = await client.GetPageAsync(profile, token, node.Id, cancellationToken);
                    results.HierarchyNodes[node.HierarchyIndex] = results.HierarchyNodes[node.HierarchyIndex] with { Title = page.Title };
                    path = node.Path.Length == 0 ? page.Title : node.Path + " / " + page.Title;
                    if (node.Id == rootId)
                    {
                        results.SourceRootTitle = page.Title;
                        results.RootContext = $"Source root: {page.Title} (page {page.Id}); hierarchy context is descriptive, not an authority signal.";
                    }
                    if (page.ExtractionStatus != ExtractionStatus.Complete || string.IsNullOrWhiteSpace(page.Content))
                    {
                        sourceContentFailed = true;
                        RecordContentSkip(results, page);
                        results.Warnings.Add($"Source page {node.Id} was skipped for matching ({page.ExtractionStatus}). Its children are still traversed. {string.Join(" ", page.Limitations)}");
                    }
                    else
                    {
                        characters += page.Content.Length;
                        CheckMemory(characters);
                        results.SourcePages.Add(new DiscoveredPage(page, path));
                    }
                }
                catch (Exception ex) when (IsPageFailure(ex))
                {
                    sourceContentFailed = true;
                    RecordReadFailure(results, ex);
                    results.Warnings.Add($"Source page {node.Id} could not be read/extracted; source content coverage is incomplete. {FailureReason(ex)}");
                }
            }
            try
            {
                await foreach (var child in discovery.GetChildrenAsync(profile, token, node.Id, node.Type, cancellationToken))
                {
                    if (scheduled.Add(child.Id))
                    {
                        if (scheduled.Count > 20000)
                            throw new InvalidDataException("The source hierarchy exceeds the 20,000-node discovery safety limit. Narrow the root; external matching was not started.");
                        int hierarchyIndex = results.HierarchyNodes.Count;
                        results.HierarchyNodes.Add(new SourceHierarchyNode(child.Id, node.Id, child.Title, child.Type,
                            results.HierarchyNodes[node.HierarchyIndex].Depth + 1));
                        queue.Enqueue((child.Id, child.Type, child.Type == "page" ? path : path + " / " + child.Title, hierarchyIndex));
                    }
                }
            }
            catch (Exception ex) when (IsPageFailure(ex))
            {
                hierarchyFailed = true;
                results.Warnings.Add($"Children of {node.Type} {node.Id} could not be completely enumerated. The source boundary is incomplete. {FailureReason(ex)}");
            }
        }
        results.SourceHierarchyComplete = !hierarchyFailed;
        results.SourceContentComplete = !sourceContentFailed;
        results.SourceComplete = results.SourceHierarchyComplete && results.SourceContentComplete;
        if (!results.SourceHierarchyComplete)
        {
            results.Warnings.Add("External discovery was not run because the source hierarchy boundary is incomplete. Check read:hierarchical-content:confluence and read:page:confluence scopes.");
            return;
        }
        if (!results.SourceContentComplete)
            results.Warnings.Add($"Source hierarchy enumeration completed, but source content coverage is incomplete. Only the {results.SourcePages.Count} successfully extracted source pages are analysed within the tree and against external pages. All visited source IDs, including unreadable or unextractable pages, remain excluded from external candidates; this is not a complete source-content scan.");
        if (results.SourcePages.Count == 0)
        {
            results.Warnings.Add("No source page text could be extracted, so there are no available source pages to compare. External discovery was not run; source content coverage is incomplete.");
            return;
        }

        var sources = results.SourcePages.Select(x => new IndexedPage(x.Page, x.Path)).ToArray();
        var postings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var titlePostings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int i = 0; i < sources.Length; i++)
        {
            foreach (string shingle in sources[i].Shingles)
                AddPosting(postings, shingle, i);
            foreach (string term in sources[i].TitleWords)
                AddPosting(titlePostings, term, i);
        }
        var findingPairs = results.Findings.Select(x => OrderedPair(x.Source.Id, x.Candidate.Id)).ToHashSet();
        var fingerprintGroups = sources.Select((page, index) => (page.Fingerprint, index))
            .GroupBy(x => x.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(x => x.index).ToArray(), StringComparer.Ordinal);
        for (int i = 0; i < sources.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = new HashSet<int>();
            foreach (string shingle in sources[i].Shingles)
                if (postings.TryGetValue(shingle, out var list))
                    foreach (int candidateIndex in list)
                        if (candidateIndex > i)
                            candidates.Add(candidateIndex);
            foreach (string term in sources[i].TitleWords)
                if (titlePostings.TryGetValue(term, out var titleMatches))
                    foreach (int candidateIndex in titleMatches)
                        if (candidateIndex > i)
                            candidates.Add(candidateIndex);
            foreach (int candidateIndex in fingerprintGroups[sources[i].Fingerprint])
                if (candidateIndex > i)
                    candidates.Add(candidateIndex);
            foreach (int candidateIndex in candidates.Order())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pair = OrderedPair(sources[i].Page.Id, sources[candidateIndex].Page.Id);
                if (!findingPairs.Add(pair))
                    continue;
                var finding = Match(sources[i], sources[candidateIndex], threshold);
                if (finding == null)
                    continue;
                if (results.Findings.Count >= 10000)
                    throw new InvalidDataException("The 10,000-finding memory limit would be exceeded. Results are partial; narrow the source hierarchy.");
                var contextualFinding = finding with
                {
                    CandidatePath = sources[candidateIndex].Path,
                    RootContext = results.RootContext,
                    SourceRootId = results.SourceRootId,
                    SourceRootTitle = results.SourceRootTitle
                };
                results.Findings.Add(contextualFinding);
                findingProgress?.Report(contextualFinding);
            }
        }

        var seen = new HashSet<string>(sourceIds, StringComparer.Ordinal);
        int pageLimit = mode == DiscoveryMode.Quick ? Math.Min(externalLimit, QuickPageLimit) : externalLimit;
        await foreach (var reference in ExternalCandidatesAsync(profile, token, spaceKeys, sourceIds, pageLimit, results, progress, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(reference.Id))
                continue;
            if (results.ExternalAttempts >= pageLimit)
            {
                results.Warnings.Add(mode == DiscoveryMode.Quick
                    ? "Quick scan body-read limit reached. Only prioritised candidates were analysed; no matches does not prove absence of duplicates. Choose Deep for a fresh, broader scan."
                    : "External page limit reached. Findings cover only a partial inventory; increase the limit to inspect more pages.");
                return;
            }
            results.ExternalAttempts++;
            try
            {
                progress.Report($"External discovery: {results.ExternalPages} pages analysed, {results.Findings.Count} candidate pairs. {results.SkipSummary}. Search covers only visible pages in the approved scope.");
                var page = await client.GetPageAsync(profile, token, reference.Id, cancellationToken);
                if (page.ExtractionStatus != ExtractionStatus.Complete || string.IsNullOrWhiteSpace(page.Content))
                {
                    RecordContentSkip(results, page);
                    results.Warnings.Add($"External page {reference.Id} was skipped for matching ({page.ExtractionStatus}); it is not an analysed non-match. {string.Join(" ", page.Limitations)}");
                    continue;
                }
                results.ExternalPages++;
                var indexed = new IndexedPage(page, string.Empty);
                var candidates = new HashSet<int>();
                foreach (string shingle in indexed.Shingles)
                    if (postings.TryGetValue(shingle, out var list))
                        candidates.UnionWith(list);
                foreach (string term in indexed.TitleWords)
                    if (titlePostings.TryGetValue(term, out var titleMatches))
                        candidates.UnionWith(titleMatches);
                for (int i = 0; i < sources.Length; i++)
                    if (sources[i].Fingerprint == indexed.Fingerprint)
                        candidates.Add(i);
                var matches = await Task.Run(() => candidates.Select(i => Match(sources[i], indexed, threshold))
                    .Where(x => x != null).Select(x => x!).ToArray(), cancellationToken);
                if (matches.Length > 0)
                {
                    characters += page.Content.Length;
                    CheckMemory(characters);
                    foreach (var match in matches)
                    {
                        var pair = OrderedPair(match.Source.Id, match.Candidate.Id);
                        if (!findingPairs.Add(pair))
                            continue;
                        if (results.Findings.Count >= 10000)
                            throw new InvalidDataException("The 10,000-finding memory limit would be exceeded. Results are partial; narrow the search scope.");
                        var contextualFinding = match with
                        {
                            RootContext = results.RootContext,
                            SourceRootId = results.SourceRootId,
                            SourceRootTitle = results.SourceRootTitle
                        };
                        results.Findings.Add(contextualFinding);
                        findingProgress?.Report(contextualFinding);
                    }
                }
            }
            catch (Exception ex) when (IsPageFailure(ex))
            {
                RecordReadFailure(results, ex);
                results.Warnings.Add($"External page {reference.Id} could not be read/extracted; it is not an analysed non-match. {FailureReason(ex)}");
            }
        }
    }

    private async IAsyncEnumerable<ContentReference> ExternalCandidatesAsync(ConnectionProfile profile, string token,
        IReadOnlyList<string> spaceKeys, HashSet<string> sourceIds, int pageLimit, DiscoveryResults results, IProgress<string> progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(sourceIds, StringComparer.Ordinal);
        var sourceTitles = results.SourcePages.Select(x => TitleTerms(x.Page.Title)).ToArray();
        var targeted = new List<ContentReference>();
        if (discovery is IConfluenceCandidateSearch search)
        {
            var queries = BuildTitleQueries(sourceTitles);
            int queryLimit = results.Mode == DiscoveryMode.Quick ? 80 : 200;
            if (queries.Count > queryLimit)
                results.Warnings.Add($"Targeted title searches limited to {queryLimit} queries. Some source titles were not searched individually; broader inventory coverage remains subject to page limits.");
            foreach (var query in queries.Take(queryLimit))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (targeted.Count >= QuickMetadataLimit) break;
                results.TargetedQueries++;
                progress.Report($"{results.Mode} scan: source-title search {results.TargetedQueries}/{Math.Min(queries.Count, queryLimit)}, {targeted.Count} unique external candidates retrieved.");
                try
                {
                    int queryResults = 0;
                    await foreach (var reference in search.SearchPagesAsync(profile, token, query, spaceKeys, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        queryResults++;
                        if (seen.Add(reference.Id))
                        {
                            targeted.Add(reference);
                            results.MetadataPages++;
                            results.TargetedCandidates++;
                        }
                        if (queryResults >= 100 || targeted.Count >= QuickMetadataLimit)
                        {
                            results.Warnings.Add("A targeted title-search result cap was reached. Search results are partial; this is not proof that all similar pages were retrieved.");
                            break;
                        }
                    }
                }
                catch (Exception ex) when (IsPageFailure(ex))
                {
                    results.SearchFailures++;
                    results.Warnings.Add($"A targeted title search failed. Inventory fallback is still bounded and may miss relevant pages. Scoped tokens need read:content-details:confluence for search. {FailureReason(ex)}");
                }
            }
        }
        else
            results.Warnings.Add("Targeted title search is unavailable from this discovery service. Only bounded inventory retrieval was used; relevant pages may be missed.");

        if (results.Mode == DiscoveryMode.Quick)
            results.Warnings.Add($"Quick scan searches source titles/identifiers first, inspects at most {QuickMetadataLimit:N0} unique external metadata entries, and reads at most {QuickPageLimit} bodies (or the lower configured limit). Retrieval is partial; differently titled or rewritten duplicates may still be missed. Content evidence is checked separately.");
        foreach (var reference in targeted.OrderByDescending(x => TitlePriority(x.Title, sourceTitles)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return reference;
        }
        if (results.ExternalAttempts >= pageLimit)
        {
            results.Warnings.Add("Body-read budget reached after targeted searches. The full inventory was not traversed; run Deep or increase the configured limit for broader coverage.");
            yield break;
        }
        if (results.Mode == DiscoveryMode.Deep)
        {
            await foreach (var reference in discovery.GetPagesAsync(profile, token, spaceKeys, cancellationToken))
                if (seen.Add(reference.Id)) yield return reference;
            results.ExternalComplete = true;
            yield break;
        }
        if (results.MetadataPages >= QuickMetadataLimit)
        {
            results.Warnings.Add("Quick metadata limit reached. Inventory exhaustion was not established.");
            yield break;
        }
        var references = new List<ContentReference>();
        bool exhausted = true;
        await foreach (var reference in discovery.GetPagesAsync(profile, token, spaceKeys, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(reference.Id)) continue;
            references.Add(reference);
            results.MetadataPages++;
            if (references.Count % 100 == 0)
                progress.Report($"Quick scan: inspecting {results.MetadataPages:N0} unique external metadata entries before remaining body reads.");
            if (results.MetadataPages == QuickMetadataLimit)
            {
                exhausted = false;
                results.Warnings.Add("Quick metadata limit reached. Inventory exhaustion was not established; title ranking covers only the inspected metadata window.");
                break;
            }
        }
        foreach (var reference in references.OrderByDescending(x => TitlePriority(x.Title, sourceTitles)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return reference;
        }
        results.ExternalComplete = exhausted;
    }

    private static List<string[]> BuildTitleQueries(HashSet<string>[] titles)
    {
        var queries = titles.SelectMany(x => x).Where(x => x.Length <= 100 && TitleIdentifierRegex().IsMatch(x))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(x => new[] { x }).ToList();
        var frequencies = titles.SelectMany(x => x).GroupBy(x => x, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var keys = queries.Select(x => string.Join(" ", x)).ToHashSet(StringComparer.Ordinal);
        foreach (var title in titles)
        {
            var terms = title.Where(x => x.Length <= 100 && !TitleIdentifierRegex().IsMatch(x))
                .OrderBy(x => frequencies[x]).ThenByDescending(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).Take(2).ToArray();
            if (terms.Length > 0 && keys.Add(string.Join(" ", terms))) queries.Add(terms);
        }
        return queries;
    }

    private static double TitlePriority(string title, HashSet<string>[] sourceTitles)
    {
        var terms = TitleTerms(title);
        return sourceTitles.Select(source =>
        {
            int common = terms.Count(source.Contains);
            int smaller = Math.Min(terms.Count, source.Count);
            int identifiers = terms.Count(x => TitleIdentifierRegex().IsMatch(x) && source.Contains(x));
            return identifiers * 1000 + (smaller == 0 ? 0 : (double)common / smaller) * 100 + common;
        }).DefaultIfEmpty().Max();
    }

    private static readonly HashSet<string> GenericTitleTerms = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "from", "this", "that", "page", "copy", "overview", "guide", "guidance",
        "documentation", "document", "reference", "integration", "integrations", "interface", "data", "sync",
        "adapter", "inbound", "outbound", "specification", "design", "technical", "functional", "draft", "version"
    };

    private static HashSet<string> TitleTerms(string title) => TitleRegex().Matches(title.ToLowerInvariant())
        .Select(x => x.Value).Where(x => x.Length > 2 && !GenericTitleTerms.Contains(x)).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\A[\p{L}]{2,12}[\p{N}]{2,8}\z")]
    private static partial Regex TitleIdentifierRegex();

    private static void AddPosting(Dictionary<string, List<int>> index, string term, int pageIndex)
    {
        if (!index.TryGetValue(term, out var pages))
            index[term] = pages = [];
        pages.Add(pageIndex);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex TitleRegex();

    private static void RecordContentSkip(DiscoveryResults results, ConfluencePage page)
    {
        results.FailedPages++;
        if (page.ExtractionStatus == ExtractionStatus.LimitExceeded) results.LimitedPages++;
        else results.EmptyPages++;
    }

    private static void RecordReadFailure(DiscoveryResults results, Exception exception)
    {
        results.FailedPages++;
        if (exception is ConfluenceException { StatusCode: System.Net.HttpStatusCode.Forbidden }) results.AccessDeniedPages++;
        else if (exception is ConfluenceException { StatusCode: System.Net.HttpStatusCode.NotFound }) results.UnavailablePages++;
        else results.RequestFailedPages++;
    }

    private static bool IsPageFailure(Exception exception) => exception is ConfluenceException { StopScan: false }
        or System.Net.Http.HttpRequestException or System.Text.Json.JsonException;

    private static string FailureReason(Exception exception) => exception is ConfluenceException confluence ? confluence.Message :
        exception is System.Text.Json.JsonException ? "Confluence returned malformed JSON." : "Network, TLS or response-size failure.";

    private static void CheckMemory(long characters)
    {
        if (characters > 15000000)
            throw new InvalidDataException("The 15-million-character in-memory scan limit was reached. Results are partial; narrow the scope. No content was silently discarded.");
    }

    private static (string First, string Second) OrderedPair(string first, string second) =>
        string.CompareOrdinal(first, second) <= 0 ? (first, second) : (second, first);

    private static DuplicateFinding? Match(IndexedPage source, IndexedPage candidate, double threshold)
    {
        bool exact = source.Fingerprint == candidate.Fingerprint;
        int common = source.Shingles.Count(candidate.Shingles.Contains);
        int union = source.Shingles.Count + candidate.Shingles.Count - common;
        double jaccard = union == 0 ? 0 : (double)common / union;
        double smallerCoverage = Math.Min(source.Shingles.Count, candidate.Shingles.Count) == 0 ? 0 :
            (double)common / Math.Min(source.Shingles.Count, candidate.Shingles.Count);
        double sourceCoverage = exact ? 100 : source.Shingles.Count == 0 ? 0 : (double)common / source.Shingles.Count * 100;
        double candidateOnly = exact ? 0 : candidate.Shingles.Count == 0 ? 0 :
            (double)(candidate.Shingles.Count - common) / candidate.Shingles.Count * 100;
        bool lexicalMatch = exact || (common >= 5 && (jaccard >= threshold || smallerCoverage >= Math.Max(0.8, threshold)));
        int sharedTitleWords = source.TitleWords.Count(candidate.TitleWords.Contains);
        int smallerTitle = Math.Min(source.TitleWords.Count, candidate.TitleWords.Count);
        double titleCoverage = smallerTitle == 0 ? 0 : (double)sharedTitleWords / smallerTitle;
        var sharedIdentifiers = source.TitleWords.Where(x => TitleIdentifierRegex().IsMatch(x) && candidate.TitleWords.Contains(x)).ToArray();
        int sharedWords = source.Words.Count(candidate.Words.Contains);
        int smallerWords = Math.Min(source.Words.Count, candidate.Words.Count);
        double wordCoverage = smallerWords == 0 ? 0 : (double)sharedWords / smallerWords;
        bool functionalOverlap = !lexicalMatch && sharedWords >= 10 && wordCoverage >= 0.15 &&
            ((sharedIdentifiers.Length > 0 && sharedTitleWords >= 2 && titleCoverage >= 0.5) ||
             (sharedTitleWords >= 3 && titleCoverage >= 0.75));
        if (!lexicalMatch && !functionalOverlap)
            return null;

        string relationship = functionalOverlap ? "Potential functional overlap" : exact ? "Exact extracted-text duplicate" :
            jaccard >= threshold ? "Possible near duplicate" : "Possible partial overlap";
        string recommendation = functionalOverlap ? "Review shared purpose with the owners" : exact ? "Review as a potential duplicate" :
            sourceCoverage >= 80 && candidateOnly <= 20 ? "Review candidate for substantial overlap" :
            sourceCoverage >= 80 || candidateOnly <= 20 ? "Compare complementary portions" : "Review as related material";
        string functionalEvidence = $"{sharedTitleWords} shared meaningful title terms; {Math.Round(titleCoverage * 100, 1)}% smaller-title term coverage; " +
            (sharedIdentifiers.Length > 0 ? $"shared title identifiers: {string.Join(", ", sharedIdentifiers)}; " : string.Empty) +
            $"{sharedWords} shared distinct content words ({Math.Round(wordCoverage * 100, 1)}% smaller-word-set coverage). " +
            "Wording differs. This flags a possible shared business purpose, not proven duplication, semantic equivalence or authority. Check direction, mappings, audience and ownership.";
        string reason = functionalOverlap ? functionalEvidence : exact
            ? "The case-sensitive, whitespace-normalised extracted texts match. Confirm page purpose and extraction limitations; neither page is designated authoritative."
            : $"{Math.Round(sourceCoverage, 1)}% of source 5-word shingles occur in the candidate; {Math.Round(candidateOnly, 1)}% of candidate shingles are candidate-only. These lexical estimates do not establish factual completeness or authority. Owner IDs and recency are context only.";
        string evidence = functionalOverlap ? functionalEvidence : exact
            ? "SHA-256 of case-sensitive, whitespace-normalised extracted text matches. Review macro/image limitations; this does not establish authority."
            : $"{common} shared distinct 5-word shingles; {union} distinct shingles across the pair. Source coverage and candidate-only content are lexical estimates, not factual completeness. Human review required.";
        return new DuplicateFinding(source.Page, candidate.Page, source.Path, relationship,
            exact ? 100 : Math.Round(jaccard * 100, 1), exact ? 100 : Math.Round(smallerCoverage * 100, 1), evidence)
        {
            MatchBasis = functionalOverlap ? "Title/identifier + content-word overlap; human review required" : exact ? "Identical extracted text" : "5-word lexical overlap",
            TitleCoverage = Math.Round(titleCoverage * 100, 1),
            SourceCoverage = Math.Round(sourceCoverage, 1),
            CandidateOnlyContentPercent = Math.Round(candidateOnly, 1),
            Recommendation = recommendation,
            RecommendationReason = reason
        };
    }

    private sealed partial class IndexedPage
    {
        public IndexedPage(ConfluencePage page, string path)
        {
            Page = page;
            Path = path;
            string normalised = string.Join(" ", page.Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
            var tokens = MyRegex().Matches(normalised.ToLowerInvariant()).Select(x => x.Value).ToArray();
            Words = tokens.Where(x => x.Length >= 3).ToHashSet(StringComparer.Ordinal);
            TitleWords = TitleTerms(page.Title);
            Shingles = Enumerable.Range(0, Math.Max(0, tokens.Length - 4))
                .Select(i => string.Join(" ", tokens, i, 5)).ToHashSet(StringComparer.Ordinal);
        }
        public ConfluencePage Page { get; }
        public string Path { get; }
        public string Fingerprint { get; }
        public HashSet<string> Shingles { get; }
        public HashSet<string> Words { get; }
        public HashSet<string> TitleWords { get; }

        [GeneratedRegex(@"[\p{L}\p{N}]+")]
        private static partial Regex MyRegex();
    }

    [GeneratedRegex(@"\A[0-9]+\z")]
    private static partial Regex MyRegex();
}
