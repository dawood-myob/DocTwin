using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DocTwin.Confluence;

// Lightweight version information returned by a bulk lookup that does not download page bodies.
public sealed record PageStamp(string Id, int Version, string Title, string SpaceId, string Updated,
    string? ParentId, string? ParentType, string? OwnerId);

public interface IPageStampSource
{
    Task<IReadOnlyDictionary<string, PageStamp>> GetStampsAsync(ConnectionProfile profile, string token,
        IReadOnlyCollection<string> pageIds, CancellationToken cancellationToken);
}

public interface IPagePrefetch
{
    Task PrefetchAsync(ConnectionProfile profile, string token, IReadOnlyCollection<string> pageIds,
        CancellationToken cancellationToken);
}

public sealed record CachedPage(string Key, string Id, string Title, string SpaceId, int Version, string Updated,
    string Content, string[] Limitations, ExtractionStatus Status, string? ParentId, string? ParentType, string? OwnerId)
{
    public static CachedPage From(string key, ConfluencePage page) => new(key, page.Id, page.Title, page.SpaceId, page.Version,
        page.Updated, page.Content, [.. page.Limitations], page.ExtractionStatus, page.ParentId, page.ParentType, page.OwnerId);

    public ConfluencePage ToPage(ConnectionProfile profile, PageStamp stamp)
    {
        static string Pick(string? fresh, string fallback) => string.IsNullOrWhiteSpace(fresh) ? fallback : fresh;
        return new ConfluencePage(Id, Pick(stamp.Title, Title), Pick(stamp.SpaceId, SpaceId), Version, Pick(stamp.Updated, Updated),
            new Uri(profile.Site, $"wiki/pages/viewpage.action?pageId={Id}"), Content, Limitations)
        {
            ParentId = stamp.ParentId ?? ParentId,
            ParentType = stamp.ParentType ?? ParentType,
            OwnerId = stamp.OwnerId ?? OwnerId,
            ExtractionStatus = Status
        };
    }
}

// Local, encrypted store of extracted page text keyed by site, page and version. It lets repeat scans skip
// downloading pages that have not changed. Cleared on demand; never contains tokens.
public sealed class PageCache
{
    private const int SchemaVersion = 1;
    private const int MaximumEntries = 6000;
    private const long MaximumCharacters = 150_000_000;
    private const long MaximumFileBytes = 250L * 1024 * 1024;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DocTwin.PageCache.v1");

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, CachedPage> _entries = new(StringComparer.Ordinal);
    private bool _loaded;
    private bool _dirty;
    private long _characters;
    private int _reused;
    private int _refreshed;
    private int _added;

    public PageCache(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetEnvironmentVariable("DOCTWIN_SETTINGS_DIR") is { Length: > 0 } dir ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocTwin"),
            "page-cache.bin"));
    }

    public int RunId { get; private set; }
    public int Reused => _reused;
    public int Refreshed => _refreshed;
    public int Added => _added;
    public string? LoadWarning { get; private set; }

    public static string Key(ConnectionProfile profile, string pageId) =>
        profile.Site.GetLeftPart(UriPartial.Authority).ToLowerInvariant() + "|" + pageId;

    public void BeginRun()
    {
        lock (_gate)
        {
            RunId++;
            _reused = _refreshed = _added = 0;
        }
    }

    public CachedPage? Find(string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _entries.GetValueOrDefault(key);
        }
    }

    public void CountReused() => Interlocked.Increment(ref _reused);

    public void Put(CachedPage page)
    {
        lock (_gate)
        {
            EnsureLoaded();
            bool replacing = _entries.TryGetValue(page.Key, out var previous);
            if (!replacing && _entries.Count >= MaximumEntries) return;
            long delta = page.Content.Length - (previous?.Content.Length ?? 0);
            if (_characters + delta > MaximumCharacters) return;
            _entries[page.Key] = page;
            _characters += delta;
            _dirty = true;
            if (replacing) _refreshed++; else _added++;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries = new Dictionary<string, CachedPage>(StringComparer.Ordinal);
            _characters = 0;
            _loaded = true;
            _dirty = false;
            _reused = _refreshed = _added = 0;
            LoadWarning = null;
            RunId++;
            if (File.Exists(_path)) File.Delete(_path);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            var payload = new CacheFile(SchemaVersion, [.. _entries.Values]);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(json);
            byte[] protectedBytes = ProtectedData.Protect(compressed.ToArray(), Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string pending = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(pending, protectedBytes);
                File.Move(pending, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
            _dirty = false;
        }
    }

    public string Describe()
    {
        lock (_gate)
        {
            if (_loaded)
                return _entries.Count == 0 ? "Page cache is empty. The first scan reads every page from Confluence." :
                    $"Page cache: {_entries.Count:N0} pages stored locally (encrypted for this Windows account).";
        }
        var info = new FileInfo(_path);
        return info.Exists
            ? $"Page cache: {info.Length / 1024.0 / 1024.0:N1} MB stored locally (encrypted for this Windows account), last updated {info.LastWriteTime:g}."
            : "Page cache is empty. The first scan reads every page from Confluence.";
    }

    public string RunSummary()
    {
        lock (_gate)
        {
            int total = _reused + _refreshed + _added;
            return total == 0 ? string.Empty :
                $"Page cache this scan: {_reused:N0} unchanged pages reused without downloading, {_refreshed:N0} changed pages re-read, {_added:N0} new pages read. Findings are always recomputed from current page versions.";
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_path)) return;
            var info = new FileInfo(_path);
            if (info.Length > MaximumFileBytes) throw new InvalidDataException("The page cache file exceeds its size limit.");
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            using var gzip = new GZipStream(new MemoryStream(plain), CompressionMode.Decompress);
            var file = JsonSerializer.Deserialize<CacheFile>(gzip);
            if (file == null || file.Schema != SchemaVersion) return;
            foreach (var entry in file.Entries.Take(MaximumEntries))
            {
                _entries[entry.Key] = entry;
                _characters += entry.Content.Length;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _entries.Clear();
            _characters = 0;
            LoadWarning = "The page cache could not be read, so it was ignored. It will be rebuilt by the next scan.";
        }
    }

    private sealed record CacheFile(int Schema, CachedPage[] Entries);
}

// Wraps the live client. A page body is downloaded only when it is new or its version changed; otherwise the
// cached text is reused after a cheap version check made with the current account's token.
public sealed class CachingConfluenceClient(IConfluenceClient inner, IPageStampSource stamps, PageCache cache) : IConfluenceClient, IPagePrefetch
{
    private readonly Dictionary<string, (PageStamp Stamp, int RunId)> _stamps = new(StringComparer.Ordinal);
    private int _stampsUnavailableRun = -1;

    public Task TestConnectionAsync(ConnectionProfile profile, string token, CancellationToken cancellationToken) =>
        inner.TestConnectionAsync(profile, token, cancellationToken);

    public async Task PrefetchAsync(ConnectionProfile profile, string token, IReadOnlyCollection<string> pageIds,
        CancellationToken cancellationToken)
    {
        var wanted = pageIds.Where(id => cache.Find(PageCache.Key(profile, id)) != null && !HasStamp(PageCache.Key(profile, id))).ToArray();
        if (wanted.Length == 0 || cache.RunId == _stampsUnavailableRun) return;
        try
        {
            var found = await stamps.GetStampsAsync(profile, token, wanted, cancellationToken);
            lock (_stamps)
                foreach (var stamp in found.Values)
                    _stamps[PageCache.Key(profile, stamp.Id)] = (stamp, cache.RunId);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            _stampsUnavailableRun = cache.RunId;
        }
    }

    public async Task<ConfluencePage> GetPageAsync(ConnectionProfile profile, string token, string pageId,
        CancellationToken cancellationToken)
    {
        string key = PageCache.Key(profile, pageId);
        var cached = cache.Find(key);
        if (cached is { Version: > 0 })
        {
            var stamp = TakeStamp(key) ?? await LookupAsync(profile, token, pageId, cancellationToken);
            if (stamp != null && stamp.Version == cached.Version)
            {
                cache.CountReused();
                return cached.ToPage(profile, stamp);
            }
        }
        var page = await inner.GetPageAsync(profile, token, pageId, cancellationToken);
        if (page.Version > 0)
            cache.Put(CachedPage.From(key, page));
        return page;
    }

    private bool HasStamp(string key)
    {
        lock (_stamps) return _stamps.TryGetValue(key, out var entry) && entry.RunId == cache.RunId;
    }

    private PageStamp? TakeStamp(string key)
    {
        lock (_stamps)
        {
            if (!_stamps.Remove(key, out var entry)) return null;
            return entry.RunId == cache.RunId ? entry.Stamp : null;
        }
    }

    private async Task<PageStamp?> LookupAsync(ConnectionProfile profile, string token, string pageId, CancellationToken cancellationToken)
    {
        if (cache.RunId == _stampsUnavailableRun) return null;
        try
        {
            var found = await stamps.GetStampsAsync(profile, token, [pageId], cancellationToken);
            return found.GetValueOrDefault(pageId);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            _stampsUnavailableRun = cache.RunId;
            return null;
        }
    }

    // A failed version check only means "download the page"; cancellation and rate-limit stops must still surface.
    private static bool IsRecoverable(Exception ex) =>
        ex is not OperationCanceledException && ex is not ConfluenceException { StopScan: true };
}
