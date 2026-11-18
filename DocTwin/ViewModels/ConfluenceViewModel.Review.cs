using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocTwin.Analysis;
using Microsoft.Win32;

namespace DocTwin.ViewModels;

public partial class ConfluenceViewModel
{
    private readonly ReviewStore _reviewStore;
    [ObservableProperty] private ReviewDecision _selectedReviewDecision = ReviewDecision.Investigate;
    [ObservableProperty] private string _reviewNotes = string.Empty;
    [ObservableProperty] private string _reviewStatus = "Select a finding to record a human review. Decisions never change Confluence.";
    public IReadOnlyList<ReviewDecision> ReviewDecisions { get; } = Enum.GetValues<ReviewDecision>();
    public ObservableCollection<ReviewRecord> ReviewHistory { get; } = [];

    public bool LoadReviewHistory()
    {
        try
        {
            var records = _reviewStore.Read();
            ReviewHistory.Clear();
            foreach (var record in records.OrderByDescending(x => x.ReviewedAt)) ReviewHistory.Add(record);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            ReviewStatus = "Local review history could not be loaded. It has not been overwritten. Check access and the reviews.json file in your local DocTwin application data folder.";
            return false;
        }
    }

    partial void OnSelectedFindingChanged(DuplicateFinding? value)
    {
        SaveReviewCommand.NotifyCanExecuteChanged();
        var review = value == null ? null : ReviewHistory.FirstOrDefault(x => x.Site == value.Source.Url.GetLeftPart(UriPartial.Authority) && x.SourceId == value.Source.Id && x.CandidateId == value.Candidate.Id);
        SelectedReviewDecision = review?.Decision ?? ReviewDecision.Investigate;
        ReviewNotes = review?.Notes ?? string.Empty;
        ReviewStatus = value == null ? "Select a finding to record a human review. Decisions never change Confluence." : review == null ?
            "Not yet reviewed. Check ownership, approved guidance, audience and evidence before choosing a preferred reference." :
            review.SourceVersion == value.Source.Version && review.CandidateVersion == value.Candidate.Version ?
            $"Saved {review.ReviewedAt:g}. Reviewed versions match this scan; they may still differ from the latest live versions." :
            "Page versions changed since the saved decision. Re-review required; the previous decision is not treated as current.";
    }

    [RelayCommand(CanExecute = nameof(CanReviewFinding))]
    private void SaveReview()
    {
        if (SelectedFinding is not { } finding) return;
        try
        {
            var record = new ReviewRecord(finding.Source.Url.GetLeftPart(UriPartial.Authority), finding.Source.Id, finding.Candidate.Id,
                finding.Source.Version, finding.Candidate.Version, SelectedReviewDecision, ReviewNotes.Trim(), DateTimeOffset.UtcNow);
            _reviewStore.Save(record);
            if (!LoadReviewHistory())
            {
                Status = "The review was written, but local history could not be refreshed. Check the review-history warning before treating the displayed decision as current.";
                return;
            }
            OnSelectedFindingChanged(finding);
            Status = "Human review saved locally. No Confluence page was edited, archived, added or deleted.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            Status = "The review could not be saved. Check file access, known positive page versions and notes length (maximum 4,000 characters). If another DocTwin instance is saving, retry after it finishes. Existing history was not intentionally replaced.";
        }
    }

    private bool CanExport() => !IsBusy && (Findings.Count > 0 || HealthRows.Count > 0 || AiQualityRows.Count > 0);

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportReport()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export DocTwin review report (may contain internal titles, links and notes)",
            Filter = "HTML review report (*.html)|*.html|CSV spreadsheet (*.csv)|*.csv",
            FileName = "DocTwin-review-" + DateTime.Now.ToString("yyyyMMdd-HHmm"),
            AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var findings = Findings.ToArray();
            var aiQuality = AiQualityRows.ToArray();
            string report = dialog.FilterIndex == 2 ? ReportExporter.Csv(findings, ScanCoverage, ScanWarnings, ReviewHistory.ToArray(), _health, aiQuality) :
                ReportExporter.Html(findings, ScanCoverage, ScanWarnings, ReviewHistory.ToArray(), _health, aiQuality);
            File.WriteAllText(dialog.FileName, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: dialog.FilterIndex == 2));
            Status = "Review report exported locally. Protect the file as internal information; no Confluence content was changed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = "Windows could not finish writing the report. Check the destination and free space; an incomplete file may exist.";
        }
    }
}
