using System.Globalization;
using System.Net;
using System.Text;
using DocTwin.AI;

namespace DocTwin.Analysis;

public static class ReportExporter
{
    public static string Csv(IReadOnlyList<DuplicateFinding> findings, string coverage, string warnings, IReadOnlyList<ReviewRecord> reviews, ContentHealthReport? health = null, IReadOnlyList<AiPageQuality>? aiQuality = null)
    {
        var output = new StringBuilder();
        output.AppendLine(Row("DocTwin read-only review report", DateTimeOffset.UtcNow.ToString("O")));
        output.AppendLine(Row("Coverage", coverage));
        if (health != null)
            output.AppendLine(Row("Content health summary", health.Summary));
        output.AppendLine(Row("Warnings", warnings));
        output.AppendLine(Row("Notice", "Lexical scores do not establish factual completeness or authority. Candidate-only text is not necessarily a missing update. No Confluence content was changed."));
        output.AppendLine(Row("Source", "Source URL", "Source version", "Source path", "Candidate", "Candidate URL", "Candidate version", "Candidate space ID", "Relationship", "Match basis", "Smaller-title coverage %", "Lexical similarity %", "Smaller-page coverage %", "Source covered %", "Candidate-only %", "Titles match", "Content verdict", "Content match %", "Word similarity %", "Differing numbers/IDs", "Suggestion", "Suggestion reason", "Evidence", "Review decision", "Review notes", "Reviewed versions current"));
        foreach (var finding in findings)
        {
            var review = FindReview(finding, reviews);
            output.AppendLine(Row(finding.SourceTitle, finding.Source.Url.AbsoluteUri, finding.Source.Version.ToString(CultureInfo.InvariantCulture), finding.SourcePath,
                finding.CandidateTitle, finding.Candidate.Url.AbsoluteUri, finding.Candidate.Version.ToString(CultureInfo.InvariantCulture), finding.CandidateSpace,
                finding.Relationship, finding.MatchBasis, finding.TitleCoverage.ToString(CultureInfo.InvariantCulture), finding.LexicalScore.ToString(CultureInfo.InvariantCulture), finding.SmallerPageCoverage.ToString(CultureInfo.InvariantCulture), finding.SourceCoverage.ToString(CultureInfo.InvariantCulture), finding.CandidateOnlyContentPercent.ToString(CultureInfo.InvariantCulture), finding.TitleMatchText, finding.ContentVerdict, finding.ContentMatchPercent?.ToString(CultureInfo.InvariantCulture) ?? "", finding.WordSimilarityPercent?.ToString(CultureInfo.InvariantCulture) ?? "", finding.DifferingFacts, finding.Recommendation, finding.RecommendationReason, finding.Evidence,
                review?.Decision.ToString() ?? "NotReviewed", review?.Notes ?? "", IsCurrent(finding, review) ? "Yes" : "No / not reviewed"));
        }
        if (health != null)
        {
            output.AppendLine();
            output.AppendLine(Row("Content health (suggestions for human review)"));
            output.AppendLine(Row("Page", "URL", "Source path", "Status", "Words", "Last updated", "Duplicate group", "Possible duplicate pages (title - URL)", "Advice"));
            foreach (var row in health.Rows)
                output.AppendLine(Row(row.Title, row.Url.AbsoluteUri, row.Path, row.Status, row.Words.ToString(CultureInfo.InvariantCulture), row.Updated, row.DuplicateGroup,
                    string.Join(" | ", row.PossibleDuplicates.Take(MaximumListedDuplicates).Select(x => x.Title + " - " + x.Url.AbsoluteUri)) +
                        (row.PossibleDuplicates.Count > MaximumListedDuplicates ? $" | and {row.PossibleDuplicates.Count - MaximumListedDuplicates} more" : string.Empty), row.Advice));
        }
        if (aiQuality is { Count: > 0 })
        {
            output.AppendLine();
            output.AppendLine(Row("AI quality advice (advisory; a person decides)"));
            output.AppendLine(Row("Page", "URL", "Source path", "AI rating", "Summary", "Issues and suggestions", "Suggested human review", "Uncertainties", "Not accepted because"));
            foreach (var row in aiQuality)
                output.AppendLine(Row(row.Title, row.Url.AbsoluteUri, row.Path, row.Rating, row.Summary, row.Issues.Replace("\n", " | "), row.SuggestedAction, row.Uncertainties, row.Error));
        }
        return output.ToString();
    }

    public static string Html(IReadOnlyList<DuplicateFinding> findings, string coverage, string warnings, IReadOnlyList<ReviewRecord> reviews, ContentHealthReport? health = null, IReadOnlyList<AiPageQuality>? aiQuality = null)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var output = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>DocTwin review report</title><style>body{font:15px Segoe UI,sans-serif;background:#f7f6f1;color:#000;margin:32px}h1{color:#7b14ef}table{border-collapse:collapse;width:100%}td,th{border:1px solid #e6e0d1;padding:12px;text-align:left;vertical-align:top}th{background:#7b14ef;color:#fff}tr:nth-child(even){background:#ebdcfd}a{color:#7b14ef}pre{white-space:pre-wrap}footer{margin-top:24px;color:#000}.bb{display:inline-block;background:linear-gradient(90deg,#7b14ef,#fe02a7);color:#fff;border-radius:12px;padding:3px 10px;font-size:12px;font-weight:600;margin-left:8px;vertical-align:middle}</style><h1>DocTwin | Evidence-first review <span class=\"bb\">#Breakthrough Build</span></h1>");
        output.Append("<p>Generated ").Append(E(DateTimeOffset.UtcNow.ToString("u"))).Append("</p><p>").Append(E(coverage)).Append("</p>");
        if (health != null)
        {
            output.Append("<h2>Content health</h2><p>").Append(E(health.Summary).Replace("\n", "<br>")).Append("</p><table><thead><tr><th>Page</th><th>Status</th><th>Words</th><th>Last updated</th><th>Duplicate group</th><th>Advice (suggestion)</th></tr></thead><tbody>");
            foreach (var row in health.Rows)
                output.Append("<tr><td><a href=\"").Append(E(row.Url.AbsoluteUri)).Append("\">").Append(E(row.Title)).Append("</a><p>").Append(E(row.Path)).Append("</p></td><td>").Append(E(row.Status)).Append("</td><td>").Append(row.Words).Append("</td><td>").Append(E(row.Updated)).Append("</td><td>").Append(E(row.DuplicateGroup)).Append(DuplicateLinks(row)).Append("</td><td>").Append(E(row.Advice)).Append("</td></tr>");
            output.Append("</tbody></table>");
        }
        if (aiQuality is { Count: > 0 })
        {
            output.Append("<h2>AI quality advice (advisory)</h2><p>Generated by an AI provider from the page text. Quotations were checked against the page text; ratings and suggestions need human review. Nothing was changed in Confluence.</p><table><thead><tr><th>Page</th><th>AI rating</th><th>Summary</th><th>Issues and suggestions</th><th>Suggested human review</th></tr></thead><tbody>");
            foreach (var row in aiQuality)
                output.Append("<tr><td><a href=\"").Append(E(row.Url.AbsoluteUri)).Append("\">").Append(E(row.Title)).Append("</a><p>").Append(E(row.Path)).Append("</p></td><td>").Append(E(row.Rating)).Append("</td><td>").Append(E(row.Summary.Length > 0 ? row.Summary : row.Error)).Append("</td><td>").Append(E(row.Issues).Replace("\n", "<br>")).Append("</td><td>").Append(E(row.SuggestedAction)).Append("<p>").Append(E(row.Uncertainties)).Append("</p></td></tr>");
            output.Append("</tbody></table>");
        }
        if (health != null || aiQuality is { Count: > 0 })
            output.Append("<h2>Duplicate candidates</h2>");
        output.Append("<pre>").Append(E(warnings)).Append("</pre><p>Potential functional overlap is a title/identifier and content-word review signal, not proven duplication or semantic equivalence. Lexical and title scores do not establish authority or factual completeness. Candidate-only text is not necessarily a missing update. No Confluence content was changed.</p><table><thead><tr><th>Source / context</th><th>Candidate</th><th>Relationship / scores</th><th>Evidence</th><th>Human review</th></tr></thead><tbody>");
        foreach (var finding in findings)
        {
            var review = FindReview(finding, reviews);
            output.Append("<tr><td><a href=\"").Append(E(finding.Source.Url.AbsoluteUri)).Append("\">").Append(E(finding.SourceTitle)).Append("</a><p>").Append(E(finding.SourcePath)).Append("</p>Version ").Append(finding.Source.Version).Append("</td><td><a href=\"").Append(E(finding.Candidate.Url.AbsoluteUri)).Append("\">").Append(E(finding.CandidateTitle)).Append("</a><p>Space ID ").Append(E(finding.CandidateSpace)).Append("</p>Version ").Append(finding.Candidate.Version).Append("</td><td>").Append(E(finding.Relationship)).Append("<p><strong>Content verdict (titles match: ").Append(E(finding.TitleMatchText)).Append("):</strong> ").Append(E(finding.ContentVerdict)).Append(finding.Content == null ? string.Empty : $"<br>Content match: {finding.Content.ContentMatchPercent.ToString(CultureInfo.InvariantCulture)}%<br>Word similarity: {finding.Content.WordCosinePercent.ToString(CultureInfo.InvariantCulture)}%<br>Differing numbers/IDs: {E(finding.DifferingFacts)}").Append("</p><p>").Append(E(finding.MatchBasis)).Append("<br>Smaller-title coverage: ").Append(finding.TitleCoverage.ToString(CultureInfo.InvariantCulture)).Append("%<br>Lexical similarity: ").Append(finding.LexicalScore.ToString(CultureInfo.InvariantCulture)).Append("%<br>Smaller-page coverage: ").Append(finding.SmallerPageCoverage.ToString(CultureInfo.InvariantCulture)).Append("%<br>Source covered: ").Append(finding.SourceCoverage.ToString(CultureInfo.InvariantCulture)).Append("%<br>Candidate-only: ").Append(finding.CandidateOnlyContentPercent.ToString(CultureInfo.InvariantCulture)).Append("%</p><strong>").Append(E(finding.Recommendation)).Append("</strong><p>").Append(E(finding.RecommendationReason)).Append("</p></td><td>").Append(E(finding.Evidence)).Append("</td><td>").Append(E(review?.Decision.ToString() ?? "NotReviewed")).Append("<p>").Append(E(review?.Notes ?? "")).Append("</p>").Append(IsCurrent(finding, review) ? "Reviewed versions match scan" : "Unreviewed or versions changed: re-review required").Append("</td></tr>");
        }
        return output.Append("</tbody></table><footer>#Breakthrough Build | Developed by Dawood Ali | Local read-only report. Review owners, approvals, audience, conflicts and original links before deciding.</footer></html>").ToString();
    }

    private const int MaximumListedDuplicates = 20;

    // Links to the other pages in the same duplicate group; empty for pages that are not in a group.
    private static string DuplicateLinks(PageHealthRow row)
    {
        if (row.PossibleDuplicates.Count == 0) return string.Empty;
        var html = new StringBuilder("<p><strong>Possible duplicates (suggestion, please review):</strong></p><ul>");
        foreach (var other in row.PossibleDuplicates.Take(MaximumListedDuplicates))
            html.Append("<li><a href=\"").Append(WebUtility.HtmlEncode(other.Url.AbsoluteUri)).Append("\">").Append(WebUtility.HtmlEncode(other.Title)).Append("</a>")
                .Append(other.InSourceTree ? string.Empty : " <small>(outside the root tree, space " + WebUtility.HtmlEncode(other.SpaceId) + ")</small>").Append("</li>");
        if (row.PossibleDuplicates.Count > MaximumListedDuplicates)
            html.Append("<li>and ").Append(row.PossibleDuplicates.Count - MaximumListedDuplicates).Append(" more</li>");
        return html.Append("</ul>").ToString();
    }

    private static ReviewRecord? FindReview(DuplicateFinding finding, IReadOnlyList<ReviewRecord> reviews) => reviews.LastOrDefault(x => x.Site == finding.Source.Url.GetLeftPart(UriPartial.Authority) && x.SourceId == finding.Source.Id && x.CandidateId == finding.Candidate.Id);
    private static bool IsCurrent(DuplicateFinding finding, ReviewRecord? review) => review != null && review.SourceVersion == finding.Source.Version && review.CandidateVersion == finding.Candidate.Version;
    private static string Row(params string[] values) => string.Join(",", values.Select(value => "\"" + Protect(value).Replace("\"", "\"\"") + "\""));
    private static string Protect(string value) => value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
}
