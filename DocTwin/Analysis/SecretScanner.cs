using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DocTwin.Analysis;

/// <summary>A possible secret. It records where and what kind, never the value itself.</summary>
public sealed record SecretMatch(string Kind, int Line, int Start, int Length);

/// <summary>
/// Local, offline detector for credentials accidentally stored in pages. It is heuristic: a clean result is not proof
/// that a page holds no secrets, and a match is a "possible secret" for a person to check. Matched values are never
/// returned, displayed, logged or sent anywhere.
/// </summary>
public static class SecretScanner
{
    public const string IncompleteKind = "Secret scan did not complete";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
    private static readonly ConditionalWeakTable<string, SecretMatch[]> ScanCache = new();
    private static readonly ConditionalWeakTable<string, string> MaskCache = new();
    private static readonly Regex DateLike = new(@"^\d{1,4}[-/.]\d{1,2}[-/.]\d{1,4}$", RegexOptions.CultureInvariant, MatchTimeout);

    private sealed record Rule(string Kind, Regex Pattern, Func<string, bool>? Accept = null, int ValueGroup = 0);

    private static Regex R(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Rule[] Rules =
    [
        new("Private key", R(@"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY(?: BLOCK)?-----[\s\S]{0,8000}?(?:-----END (?:[A-Z0-9]+ )*PRIVATE KEY(?: BLOCK)?-----|$)")),
        new("AWS access key ID", R(@"\b(?:AKIA|ASIA|AGPA|AIDA|AROA|ANPA)[0-9A-Z]{16}\b")),
        new("GitHub token", R(@"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})")),
        new("Atlassian API token", R(@"\bAT(?:ATT|CTT)[A-Za-z0-9_=\-]{20,}")),
        new("Slack token", R(@"\bxox[abposr]-[A-Za-z0-9\-]{10,}")),
        new("AI provider API key", R(@"\bsk-(?:ant-|proj-)?[A-Za-z0-9_\-]{20,}"), v => v.Any(char.IsAsciiDigit)),
        new("Google API key", R(@"\bAIza[0-9A-Za-z_\-]{35}")),
        new("Stripe live key", R(@"\b[sr]k_live_[0-9A-Za-z]{16,}")),
        new("JSON web token", R(@"\beyJ[A-Za-z0-9_\-]{8,}\.eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}")),
        new("Password inside a URL", R(@"\b[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s/:@]+:([^\s/@]{3,})@[^\s/]+"), NotPlaceholder, 1),
        new("Connection string secret", R(@"\b(?:AccountKey|SharedAccessKey|Password|Pwd)\s*=\s*([^;\s""']{4,})", RegexOptions.IgnoreCase), NotPlaceholder, 1),
        new("Password or secret value", R(@"\b(?:pass(?:word|wd|phrase)?|pwd|secret|client[_ -]?secret|api[_ -]?key|access[_ -]?key|secret[_ -]?key|auth[_ -]?token|access[_ -]?token|bearer[_ -]?token|token|credentials?)\b[ \t]*[:=][ \t]*[""'`]?([^\s""'`<>,;]{6,})", RegexOptions.IgnoreCase), LikelySecretValue, 1),
        new("Long random string near a key word", R(@"\b(?:key|secret|token|password|pwd|credential)s?\b[^\n]{0,30}?(?<![A-Za-z0-9+/_\-])([A-Za-z0-9+/_\-]{32,}={0,2})", RegexOptions.IgnoreCase), LooksRandom, 1)
    ];

    public static IReadOnlyList<SecretMatch> Scan(string? text) =>
        string.IsNullOrEmpty(text) ? [] : ScanCache.GetValue(text, Detect);

    public static bool HasSecrets(string? text) => Scan(text).Count > 0;

    // Detected values are replaced by their kind. If the scan could not finish, nothing from the text is shown.
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return MaskCache.GetValue(text, value =>
        {
            var matches = Scan(value);
            if (matches.Count == 0) return value;
            if (matches.Any(x => x.Kind == IncompleteKind)) return "[Content hidden: the secret scan did not complete]";
            var builder = new StringBuilder();
            int position = 0;
            foreach (var match in matches.OrderBy(x => x.Start))
            {
                int end = match.Start + match.Length;
                if (match.Start < position) { position = Math.Max(position, end); continue; }
                builder.Append(value, position, match.Start - position).Append("[REDACTED: ").Append(match.Kind).Append(']');
                position = end;
            }
            return builder.Append(value, position, value.Length - position).ToString();
        });
    }

    // One short phrase per kind with its first line number; never any part of the value.
    public static string Describe(IEnumerable<SecretMatch> matches) => string.Join(", ",
        matches.GroupBy(x => x.Kind).Select(g => g.Key == IncompleteKind ? g.Key : $"{g.Key}, near line {g.Min(x => x.Line)}").Take(4));

    private static SecretMatch[] Detect(string text)
    {
        var found = new List<SecretMatch>();
        foreach (var rule in Rules)
        {
            try
            {
                foreach (Match match in rule.Pattern.Matches(text))
                {
                    string value = rule.ValueGroup > 0 ? match.Groups[rule.ValueGroup].Value : match.Value;
                    if (rule.Accept == null || rule.Accept(value))
                        found.Add(new SecretMatch(rule.Kind, 0, match.Index, match.Length));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Fail closed: an unfinished scan is treated as a possible secret.
                found.Add(new SecretMatch(IncompleteKind, 1, 0, 0));
            }
        }
        if (found.Count == 0) return [];
        var lineStarts = new List<int>();
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') lineStarts.Add(i);
        return [.. found.Select(x =>
        {
            int index = lineStarts.BinarySearch(x.Start);
            return x with { Line = 1 + (index >= 0 ? index : ~index) };
        })];
    }

    private static bool NotPlaceholder(string value)
    {
        value = value.TrimEnd('.', ')', ']');
        if (value.Length < 4 || value.All(c => c is '*' or 'x' or 'X' or '•' or '·' or '_' or '-' or '.' or '#')) return false;
        if (value[0] is '$' or '%' or '{' or '[' or '(' or '<') return false;
        string lower = value.ToLowerInvariant();
        return lower is not ("password" or "passwd" or "secret" or "pass" or "pwd") &&
            !lower.Contains("your", StringComparison.Ordinal) && !lower.Contains("example", StringComparison.Ordinal) &&
            !lower.Contains("placeholder", StringComparison.Ordinal) && !lower.Contains("redacted", StringComparison.Ordinal);
    }

    // Plain words after "password:" are usually prose, so a value needs a digit or a symbol to count.
    private static bool LikelySecretValue(string value)
    {
        if (!NotPlaceholder(value) || value.Length < 6 || value.StartsWith("http", StringComparison.OrdinalIgnoreCase) || DateLike.IsMatch(value))
            return false;
        return value.Any(char.IsAsciiDigit) || value.IndexOfAny("!@#$%^&*?+=~|".ToCharArray()) >= 0;
    }

    private static bool LooksRandom(string value)
    {
        if (!value.Any(char.IsAsciiDigit) || !value.Any(char.IsAsciiLetter) || value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        double entropy = value.GroupBy(c => c).Sum(g => { double p = (double)g.Count() / value.Length; return -p * Math.Log2(p); });
        return entropy >= 3.5;
    }
}

/// <summary>Last line of defence before page content can reach an AI provider. It fails closed.</summary>
public static class SensitiveContentGuard
{
    public static void EnsureClean(params string?[] texts)
    {
        var matches = texts.SelectMany(x => SecretScanner.Scan(x)).ToArray();
        if (matches.Length == 0) return;
        throw new InvalidDataException(
            $"Possible secret detected ({SecretScanner.Describe(matches)}). DocTwin never sends content that may contain passwords or keys to an AI provider. Nothing was sent. Remove or rotate the secret in Confluence, then scan again.");
    }
}
