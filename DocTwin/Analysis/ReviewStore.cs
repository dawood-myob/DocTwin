using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocTwin.Persistence;

namespace DocTwin.Analysis;

public enum ReviewDecision { NotReviewed, Investigate, AwaitOwner, PreferSource, PreferCandidate, KeepBoth, Ignore }

public sealed record ReviewRecord(string Site, string SourceId, string CandidateId, int SourceVersion, int CandidateVersion,
    ReviewDecision Decision, string Notes, DateTimeOffset ReviewedAt);

public sealed class ReviewStore(string? path = null)
{
    private readonly string _path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocTwin", "reviews.json"));
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public IReadOnlyList<ReviewRecord> Read()
    {
        if (!File.Exists(_path)) return [];
        var records = LocalJsonFile.Read<List<ReviewRecord>>(_path, 8 * 1024 * 1024, Options) ?? throw new JsonException("Review history is empty or malformed.");
        if (records.Count > 10000 || records.Any(x => !IsValid(x)) ||
            records.Select(x => (x.Site, x.SourceId, x.CandidateId)).Distinct().Count() != records.Count)
            throw new InvalidDataException("Local review history contains invalid records. No history was overwritten.");
        return records;
    }
    public void Save(ReviewRecord record)
    {
        if (!IsValid(record)) throw new ArgumentException("A review requires a valid HTTPS Confluence site, different numeric page IDs, known positive page versions, a timestamp, a supported decision and notes of at most 4,000 characters.", nameof(record));
        using var lease = LocalJsonFile.AcquireWriteLock(_path);
        var records = Read().Where(x => !(x.Site == record.Site && x.SourceId == record.SourceId && x.CandidateId == record.CandidateId)).Append(record).ToList();
        if (records.Count > 10000) throw new InvalidDataException("Review history is limited to 10,000 pairs; no existing decisions were removed.");
        LocalJsonFile.Write(_path, JsonSerializer.Serialize(records, Options), 8 * 1024 * 1024);
    }

    private static bool IsValid(ReviewRecord? record) => record != null &&
        Uri.TryCreate(record.Site, UriKind.Absolute, out var site) && site.Scheme == Uri.UriSchemeHttps &&
        site.IsDefaultPort && site.UserInfo.Length == 0 && site.Query.Length == 0 && site.Fragment.Length == 0 &&
        site.AbsolutePath == "/" && site.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase) &&
        IsPageId(record.SourceId) && IsPageId(record.CandidateId) &&
        record.SourceId.TrimStart('0') != record.CandidateId.TrimStart('0') &&
        record.SourceVersion > 0 && record.CandidateVersion > 0 && record.ReviewedAt != default &&
        record.Notes != null && record.Notes.Length <= 4000 && Enum.IsDefined(record.Decision);

    private static bool IsPageId(string? value) => value is { Length: > 0 and <= 64 } &&
        value.All(char.IsAsciiDigit) && value.Any(character => character != '0');
}
