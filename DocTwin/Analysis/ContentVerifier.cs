using System.Text.RegularExpressions;

namespace DocTwin.Analysis;

public enum ContentVerdict { Identical, Duplicate, NearDuplicate, Contained, PartialOverlap, DifferentContent }

public sealed record ContentAssessment(ContentVerdict Verdict, double ContentMatchPercent, double WordCosinePercent,
    double SourceMatchedPercent, double CandidateMatchedPercent, IReadOnlyList<string> SourceOnlyFacts,
    IReadOnlyList<string> CandidateOnlyFacts, string Explanation, string NextStep, string Limitation)
{
    public int Rank => (int)Verdict;
    public string Label => Verdict switch
    {
        ContentVerdict.Identical => "Duplicate - identical content",
        ContentVerdict.Duplicate => "Duplicate - same content",
        ContentVerdict.NearDuplicate => "Near duplicate - differences to reconcile",
        ContentVerdict.Contained => "One page is contained in the other",
        ContentVerdict.PartialOverlap => "Partial overlap only",
        _ => "Not a duplicate - same title, different content"
    };
    public string DifferingFacts => SourceOnlyFacts.Count + CandidateOnlyFacts.Count == 0 ? "None" :
        $"Source only: {Join(SourceOnlyFacts)}; candidate only: {Join(CandidateOnlyFacts)}";
    private static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "-" : string.Join(", ", values);
}

/// <summary>
/// Second-stage engine. When two page titles match, it ignores the title and judges the inner content:
/// block-level matching (exact, then fuzzy), word-frequency cosine similarity and numeric/identifier facts.
/// It is independent of the 5-word shingle index used to find candidates.
/// </summary>
public static partial class ContentVerifier
{
    private const int FuzzyPairBudget = 400_000;
    private const double FuzzyBlockThreshold = 0.6;
    private const int FactDisplayLimit = 8;

    public static string TitleKey(string title)
    {
        var tokens = TokenRegex().Matches(title.ToLowerInvariant()).Select(x => x.Value).ToList();
        if (tokens.Count > 1 && tokens[0] == "copy" && tokens[1] == "of")
            tokens.RemoveRange(0, 2);
        while (tokens.Count > 1 && (tokens[^1] == "copy" || (tokens[^1].All(char.IsAsciiDigit) && tokens[^1].Length <= 2 && TrailingCounter(title))))
            tokens.RemoveAt(tokens.Count - 1);
        tokens.Sort(StringComparer.Ordinal);
        return string.Join(" ", tokens);
    }

    public static bool TitlesMatch(string first, string second)
    {
        string a = TitleKey(first);
        return a.Length > 0 && a == TitleKey(second);
    }

    // Only "Title (2)" style suffixes are treated as copy counters; "Release 2" is part of the title.
    private static bool TrailingCounter(string title) => TrailingCounterRegex().IsMatch(title);

    public static ContentAssessment Assess(string sourceText, string candidateText)
    {
        var source = Block.Parse(sourceText);
        var candidate = Block.Parse(candidateText);
        double sourceTotal = source.Sum(x => (double)x.Length);
        double candidateTotal = candidate.Sum(x => (double)x.Length);
        var sourceFacts = Facts(source);
        var candidateFacts = Facts(candidate);
        var sourceOnly = sourceFacts.Except(candidateFacts).Order(StringComparer.Ordinal).ToArray();
        var candidateOnly = candidateFacts.Except(sourceFacts).Order(StringComparer.Ordinal).ToArray();
        string limitation = "Block, word-frequency and fact comparison of extracted text only; images, attachments and macros are not compared. Not a judgement of accuracy or authority.";

        if (sourceTotal == 0 || candidateTotal == 0)
            return new ContentAssessment(ContentVerdict.DifferentContent, 0, 0, 0, 0, sourceOnly.Take(FactDisplayLimit).ToArray(),
                candidateOnly.Take(FactDisplayLimit).ToArray(), "One page has no comparable text.", "No duplicate action; review the empty page separately.", limitation);

        if (string.Join(" ", source.Select(x => x.Normalised)) == string.Join(" ", candidate.Select(x => x.Normalised)))
            return new ContentAssessment(ContentVerdict.Identical, 100, 100, 100, 100, [], [],
                "Every word is the same in the same order; only case, punctuation or layout can differ.",
                "Confirm which page is canonical, then ask an owner to review the other for archiving.", limitation);

        (double sourceCredit, double candidateCredit, bool fuzzySkipped) = MatchBlocks(source, candidate);
        double sourcePercent = sourceCredit / sourceTotal * 100;
        double candidatePercent = candidateCredit / candidateTotal * 100;
        double match = (sourceCredit + candidateCredit) / (sourceTotal + candidateTotal) * 100;
        double cosine = Cosine(source, candidate) * 100;
        int factUnion = sourceFacts.Count + candidateFacts.Count - sourceFacts.Intersect(candidateFacts).Count();
        string facts = factUnion == 0 ? "no numbers or identifiers" :
            sourceOnly.Length + candidateOnly.Length == 0 ? "all numbers and identifiers agree" :
            $"{sourceOnly.Length + candidateOnly.Length} numbers/identifiers differ";
        string measures = $"{Math.Round(match, 1)}% of the text matches block by block ({Math.Round(sourcePercent, 1)}% of the source, {Math.Round(candidatePercent, 1)}% of the candidate), word-frequency similarity {Math.Round(cosine, 1)}%, {facts}.";
        if (fuzzySkipped)
            limitation += " Reworded-block matching was skipped because the pages are very large.";

        var verdict = match >= 95 && cosine >= 97 && sourceOnly.Length + candidateOnly.Length == 0 ? ContentVerdict.Duplicate :
            match >= 75 && cosine >= 85 ? ContentVerdict.NearDuplicate :
            Math.Max(sourcePercent, candidatePercent) >= 90 ? ContentVerdict.Contained :
            match >= 40 || cosine >= 60 ? ContentVerdict.PartialOverlap : ContentVerdict.DifferentContent;
        string subject = sourcePercent >= candidatePercent ? "The source" : "The candidate";
        string explanation = verdict switch
        {
            ContentVerdict.Duplicate => "The content is effectively the same. " + measures,
            ContentVerdict.NearDuplicate => "Most content is shared but there are differences. " + measures,
            ContentVerdict.Contained => $"{subject} page's content is almost entirely inside the other page, which holds extra material. " + measures,
            ContentVerdict.PartialOverlap => "Some content is shared but each page also covers different material. " + measures,
            _ => "The titles match but the inner content does not. " + measures
        };
        string next = verdict switch
        {
            ContentVerdict.Duplicate => "Confirm which page is canonical, then ask an owner to review the other for archiving.",
            ContentVerdict.NearDuplicate => "Ask an owner to reconcile the content and confirm the canonical page.",
            ContentVerdict.Contained => "Ask an owner whether the smaller page is superseded or deliberately separate.",
            ContentVerdict.PartialOverlap => "Review whether the shared sections should live on one page; keep both if purposes differ.",
            _ => "Likely not a duplicate; keep both unless an owner says otherwise. Consider a clearer title."
        };
        return new ContentAssessment(verdict, Math.Round(match, 1), Math.Round(cosine, 1), Math.Round(sourcePercent, 1),
            Math.Round(candidatePercent, 1), sourceOnly.Take(FactDisplayLimit).ToArray(), candidateOnly.Take(FactDisplayLimit).ToArray(),
            explanation, next, limitation);
    }

    private static (double SourceCredit, double CandidateCredit, bool FuzzySkipped) MatchBlocks(Block[] source, Block[] candidate)
    {
        double sourceCredit = 0, candidateCredit = 0;
        var available = new Dictionary<string, Stack<int>>(StringComparer.Ordinal);
        for (int i = 0; i < candidate.Length; i++)
        {
            if (!available.TryGetValue(candidate[i].Normalised, out var stack))
                available[candidate[i].Normalised] = stack = new Stack<int>();
            stack.Push(i);
        }
        var candidateUsed = new bool[candidate.Length];
        var unmatchedSource = new List<int>();
        for (int i = 0; i < source.Length; i++)
        {
            if (available.TryGetValue(source[i].Normalised, out var stack) && stack.Count > 0)
            {
                int j = stack.Pop();
                candidateUsed[j] = true;
                sourceCredit += source[i].Length;
                candidateCredit += candidate[j].Length;
            }
            else
                unmatchedSource.Add(i);
        }
        var unmatchedCandidate = Enumerable.Range(0, candidate.Length).Where(j => !candidateUsed[j]).ToList();
        if (unmatchedSource.Count == 0 || unmatchedCandidate.Count == 0)
            return (sourceCredit, candidateCredit, false);
        if ((long)unmatchedSource.Count * unmatchedCandidate.Count > FuzzyPairBudget)
            return (sourceCredit, candidateCredit, true);

        var taken = new HashSet<int>();
        foreach (int i in unmatchedSource)
        {
            double best = 0;
            int bestIndex = -1;
            foreach (int j in unmatchedCandidate)
            {
                if (taken.Contains(j)) continue;
                double dice = Dice(source[i].Words, candidate[j].Words);
                if (dice > best) { best = dice; bestIndex = j; }
            }
            if (bestIndex < 0 || best < FuzzyBlockThreshold) continue;
            taken.Add(bestIndex);
            sourceCredit += best * source[i].Length;
            candidateCredit += best * candidate[bestIndex].Length;
        }
        return (sourceCredit, candidateCredit, false);
    }

    private static double Dice(HashSet<string> first, HashSet<string> second)
    {
        if (first.Count == 0 || second.Count == 0) return 0;
        var (small, large) = first.Count <= second.Count ? (first, second) : (second, first);
        int common = small.Count(large.Contains);
        return 2.0 * common / (first.Count + second.Count);
    }

    private static double Cosine(Block[] source, Block[] candidate)
    {
        var left = Frequencies(source);
        var right = Frequencies(candidate);
        double dot = 0;
        foreach (var (word, count) in left)
            if (right.TryGetValue(word, out int other))
                dot += (double)count * other;
        double magnitude = Math.Sqrt(left.Values.Sum(x => (double)x * x)) * Math.Sqrt(right.Values.Sum(x => (double)x * x));
        return magnitude == 0 ? 0 : dot / magnitude;
    }

    private static Dictionary<string, int> Frequencies(Block[] blocks)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var block in blocks)
            foreach (string token in block.Tokens)
                counts[token] = counts.GetValueOrDefault(token) + 1;
        return counts;
    }

    // Numbers, versions, dates and identifiers are the facts most likely to make two similar pages disagree.
    private static HashSet<string> Facts(Block[] blocks) => blocks.SelectMany(x => x.Tokens)
        .Where(x => x.Any(char.IsAsciiDigit)).ToHashSet(StringComparer.Ordinal);

    private sealed class Block
    {
        private Block(string[] tokens)
        {
            Tokens = tokens;
            Normalised = string.Join(" ", tokens);
            Words = tokens.ToHashSet(StringComparer.Ordinal);
            Length = Normalised.Length;
        }
        public string[] Tokens { get; }
        public string Normalised { get; }
        public HashSet<string> Words { get; }
        public int Length { get; }

        public static Block[] Parse(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => new Block(TokenRegex().Matches(line.ToLowerInvariant()).Select(x => x.Value).ToArray()))
            .Where(x => x.Tokens.Length > 0).ToArray();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\(\s*\d{1,2}\s*\)\s*\z")]
    private static partial Regex TrailingCounterRegex();
}
