using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace DocTwin.Confluence;

public enum ExtractionStatus { Complete, NoText, LimitExceeded }

public sealed record ExtractedContent(string Text, IReadOnlyList<string> Limitations)
{
    public ExtractionStatus Status { get; init; }
}

public sealed class PageContentExtractor
{
    public const int MaximumCharacters = 200000;
    public const int MaximumBlocks = 10000;
    public const int MaximumBlockCharacters = 8000;

    public ExtractedContent Extract(string html)
    {
        using var document = new HtmlParser().ParseDocument(html);
        var limitations = new List<string>();
        if (document.All.Any(element => element.LocalName.Contains("macro", StringComparison.OrdinalIgnoreCase)))
            limitations.Add("Confluence macros are present. Dynamic or included content may not be extracted completely; check the original pages.");
        if (document.QuerySelector("img, ac\\:image") != null)
            limitations.Add("Images and diagrams are not compared. No OCR is performed.");

        foreach (var element in document.QuerySelectorAll("script, style, nav"))
            element.Remove();

        var output = new StringBuilder();
        if (document.Body != null)
            Append(document.Body, output);

        var lines = output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(line => string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(line => line.Length > 0).ToArray();
        var text = string.Join(Environment.NewLine, lines);
        if (text.Length == 0)
            limitations.Add("No extractable text is available. The page may be empty or rely on unsupported macros or images; it must not be treated as an analysed non-match or duplicate.");
        int longestBlock = lines.Length == 0 ? 0 : lines.Max(line => line.Length);
        if (text.Length > MaximumCharacters || lines.Length > MaximumBlocks || longestBlock > MaximumBlockCharacters)
        {
            limitations.Add($"Skipped: extraction safety limit exceeded. Extracted characters: {text.Length:N0} (maximum {MaximumCharacters:N0}); text blocks: {lines.Length:N0} (maximum {MaximumBlocks:N0}); longest block: {longestBlock:N0} characters (maximum {MaximumBlockCharacters:N0}). No truncated content was analysed. This is an application processing limit, not a permission denial.");
            return new ExtractedContent(string.Empty, limitations) { Status = ExtractionStatus.LimitExceeded };
        }
        limitations.Add("Comparison uses extracted storage-format text. Attachments, images, comments, dynamic macros and page history are not compared. Page text is not executed or rendered as HTML.");
        return new ExtractedContent(text, limitations) { Status = text.Length == 0 ? ExtractionStatus.NoText : ExtractionStatus.Complete };
    }

    private static void Append(INode node, StringBuilder output)
    {
        if (node is IText text)
        {
            output.Append(text.Data);
            return;
        }

        var tag = (node as IElement)?.LocalName ?? string.Empty;
        if (tag == "br")
        {
            output.AppendLine();
            return;
        }
        bool block = tag is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "li" or "tr" or "pre" or "blockquote";
        if (block)
            output.AppendLine();
        if (tag == "li")
            output.Append("- ");
        foreach (var child in node.ChildNodes)
            Append(child, output);
        if (tag is "td" or "th")
            output.Append(" | ");
        if (block)
            output.AppendLine();
    }
}
