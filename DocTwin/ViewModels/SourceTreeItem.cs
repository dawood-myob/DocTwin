using System.Collections.ObjectModel;
using DocTwin.Analysis;
using DocTwin.Confluence;

namespace DocTwin.ViewModels;

public sealed class SourceTreeItem(string id, string title, string type, ConfluencePage? page)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Type { get; } = type;
    public string Label => $"{Title} ({Type})";
    public string Details => page == null ? $"{Title}\n{Type} {Id}\nNo analysed page text is available for this node. Read the scan coverage warnings; containers are traversed but are not analysed as pages." :
        $"{page.Title}\nPage {page.Id} | Space ID {page.SpaceId} | Version {page.Version}\nUpdated: {page.Updated}\nOwner account ID: {page.OwnerId ?? "Not supplied"} (does not prove approval)\n\nExtracted key-text preview (first 800 characters; possible secrets are masked; full text is used for analysis within limits):\n{Preview(page.Content)}\n\nExtraction limitations:\n{string.Join("\n", page.Limitations)}";

    // Masking runs on the whole text first so a secret that straddles the cut-off is still hidden.
    private static string Preview(string content)
    {
        string masked = SecretScanner.Mask(content);
        return masked[..Math.Min(masked.Length, 800)];
    }
    public ObservableCollection<SourceTreeItem> Children { get; } = [];
}
