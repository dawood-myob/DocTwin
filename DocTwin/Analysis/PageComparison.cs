using System.Security.Cryptography;
using System.Text;
using DocTwin.Confluence;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace DocTwin.Analysis;

public sealed record ComparisonRow(string Kind, string SourceLine, string SourceText,
    string CandidateLine, string CandidateText);

public sealed record PageComparisonResult(bool Identical, int ChangedLines, int TotalLines,
    IReadOnlyList<ComparisonRow> Rows);

public sealed class PageComparison
{
    public PageComparisonResult Compare(ConfluencePage source, ConfluencePage candidate)
    {
        if (source.ExtractionStatus != ExtractionStatus.Complete || candidate.ExtractionStatus != ExtractionStatus.Complete ||
            string.IsNullOrWhiteSpace(source.Content) || string.IsNullOrWhiteSpace(candidate.Content))
            throw new ArgumentException("Both pages must contain usable extracted text. Empty, unsupported or size-limited pages cannot be compared; inspect the original pages and extraction warnings.");
        // The displayed diff never shows possible secrets; the identical check below still uses the real text.
        var diff = new SideBySideDiffBuilder(new Differ()).BuildDiffModel(SecretScanner.Mask(source.Content), SecretScanner.Mask(candidate.Content));
        var rows = new List<ComparisonRow>();
        int changed = 0;
        int total = 0;
        for (int i = 0; i < diff.OldText.Lines.Count; i++)
        {
            var left = diff.OldText.Lines[i];
            var right = diff.NewText.Lines[i];
            if (left.Type != ChangeType.Imaginary)
                total++;
            if (right.Type != ChangeType.Imaginary)
                total++;
            if (left.Type is not (ChangeType.Unchanged or ChangeType.Imaginary))
                changed++;
            if (right.Type is not (ChangeType.Unchanged or ChangeType.Imaginary))
                changed++;

            string kind = left.Type == ChangeType.Imaginary ? "Candidate only" :
                right.Type == ChangeType.Imaginary ? "Source only" :
                left.Type == ChangeType.Unchanged && right.Type == ChangeType.Unchanged ? "Unchanged" : "Changed";
            rows.Add(new ComparisonRow(kind, left.Position?.ToString() ?? string.Empty, left.Text ?? string.Empty,
                right.Position?.ToString() ?? string.Empty, right.Text ?? string.Empty));
        }
        bool identical = SHA256.HashData(Encoding.UTF8.GetBytes(source.Content))
            .AsSpan().SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Content)));
        return new PageComparisonResult(identical, changed, total, rows);
    }
}
