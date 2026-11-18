using System.Globalization;
using System.Net;
using System.Text;

namespace DocTwin.Analysis;

public static class ReportExporter
{
    public static string Csv(IReadOnlyList<DuplicateFinding> findings, string coverage, string warnings, IReadOnlyList<ReviewRecord> reviews)
    {
        var output = new StringBuilder();
        output.AppendLine(Row("DocTwin read-only review report", DateTimeOffset.UtcNow.ToString("O")));
        output.AppendLine(Row("Coverage", coverage));
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
        return output.ToString();
    }

    public static string Html(IReadOnlyList<DuplicateFinding> findings, string coverage, string warnings, IReadOnlyList<ReviewRecord> reviews)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var output = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>DocTwin review report</title><style>body{font:15px Segoe UI,sans-serif;background:#f8fafc;color:#0f172a;margin:32px}h1{color:#047857}table{border-collapse:collapse;width:100%}td,th{border:1px solid #cbd5e1;padding:12px;text-align:left;vertical-align:top}th{background:#e2e8f0;border-bottom:3px solid #059669}tr:nth-child(even){background:white}a{color:#047857}pre{white-space:pre-wrap}footer{margin-top:24px;color:#64748b}</style><h1>DocTwin | Evidence-first review</h1>");
        output.Append("<p>Generated ").Append(E(DateTimeOffset.UtcNow.ToString("u"))).Append("</p><p>").Append(E(coverage)).Append("</p><pre>").Append(E(warnings)).Append("</pre><p>Potential functional overlap is a title/identifier and content-word review signal, not proven duplication or semantic equivalence. Lexical and title scores do not establish authority or factual completeness. Candidate-only text is not necessarily a missing update. No Confluence content was changed.</p><table><thead><tr><th>Source / context</th><th>Candidate</th><th>Relationship / scores</th><th>Evidence</th><th>Human review</th></tr></thead><tbody>");
        foreach (var finding in findings)
        {
            var review = FindReview(finding, reviews);
            output.Append("<tr><td><a href=\"").Append(E(finding.Source.Url.AbsoluteUri)).Append("\">").Append(E(finding.SourceTitle)).Append("</a><p>").Append(E(finding.SourcePath)).Append("</p>Version ").Append(finding.Source.Version).Append("</td><td><a href=\"").Append(E(finding.Candidate.Url.AbsoluteUri)).Append("\">").Append(E(finding.CandidateTitle)).Append("</a><p>Space ID ").Append(E(finding.CandidateSpace)).Append("</p>Version ").Append(finding.Candidate.Version).Append("</td><td>").Append(E(finding.Relationship)).Append("<p><strong>Content verdict (titles match: ").Append(E(finding.TitleMatchText)).Append("):</strong> ").Append(E(finding.ContentVerdict)).Append(finding.Content == null ? string.Empty : $"<br>Content match: {finding.Content.ContentMatchPercent.ToString(CultureInfo.InvariantCulture)}%<br>Word similarity: {finding.Content.WordCosinePercent.ToString(CultureInfo.InvariantCulture)}%<br>Differing numbers/IDs: {E(finding.DifferingFacts)}").Append("</p><p>").Append(E(finding.MatchBasis)).Append("<br>Smaller-title coverage: ").Append(finding.TitleCoverage.ToString(CultureInfo.InvariantCulture)).Append("%<br>Lexical similarity: ").Append(finding.LexicalScore.ToString(CultureInfo.InvariantCulture)).Append("%<br>Smaller-page coverage: ").Append(finding.SmallerPageCoverage.ToString(CultureInfo.InvariantCulture)).Append("%<br>Source covered: ").Append(finding.SourceCoverage.ToString(CultureInfo.InvariantCulture)).Append("%<br>Candidate-only: ").Append(finding.CandidateOnlyContentPercent.ToString(CultureInfo.InvariantCulture)).Append("%</p><strong>").Append(E(finding.Recommendation)).Append("</strong><p>").Append(E(finding.RecommendationReason)).Append("</p></td><td>").Append(E(finding.Evidence)).Append("</td><td>").Append(E(review?.Decision.ToString() ?? "NotReviewed")).Append("<p>").Append(E(review?.Notes ?? "")).Append("</p>").Append(IsCurrent(finding, review) ? "Reviewed versions match scan" : "Unreviewed or versions changed: re-review required").Append("</td></tr>");
        }
        return output.Append("</tbody></table><footer>Developed by Dawood Ali | Local read-only report. Review owners, approvals, audience, conflicts and original links before deciding.</footer></html>").ToString();
    }

    private static ReviewRecord? FindReview(DuplicateFinding finding, IReadOnlyList<ReviewRecord> reviews) => reviews.LastOrDefault(x => x.Site == finding.Source.Url.GetLeftPart(UriPartial.Authority) && x.SourceId == finding.Source.Id && x.CandidateId == finding.Candidate.Id);
    private static bool IsCurrent(DuplicateFinding finding, ReviewRecord? review) => review != null && review.SourceVersion == finding.Source.Version && review.CandidateVersion == finding.Candidate.Version;
    private static string Row(params string[] values) => string.Join(",", values.Select(value => "\"" + Protect(value).Replace("\"", "\"\"") + "\""));
    private static string Protect(string value) => value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
}
