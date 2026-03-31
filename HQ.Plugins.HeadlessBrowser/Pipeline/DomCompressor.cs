using System.Text;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static class DomCompressor
{
    private static readonly HashSet<string> WrapperTags = new()
    {
        "div", "span", "section"
    };

    private static readonly HashSet<string> NoisyClassPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "ad-", "ad_", "ads-", "ads_", "advert", "tracking", "analytics",
        "google-ad", "dfp-", "sponsored", "promo-banner"
    };

    public static DomNode Compress(DomNode root)
    {
        if (root == null) return null;

        // Apply transforms bottom-up
        root = RemoveNoiseNodes(root);
        root = CollapseWrappers(root);
        root = RemoveEmptyNodes(root);

        return root;
    }

    public static string Serialize(DomNode node, int indent = 0)
    {
        if (node == null) return string.Empty;

        var sb = new StringBuilder();
        SerializeNode(node, sb, indent);
        return sb.ToString().TrimEnd();
    }

    private static void SerializeNode(DomNode node, StringBuilder sb, int indent)
    {
        var prefix = new string(' ', indent * 2);

        if (node.IsText)
        {
            sb.AppendLine($"{prefix}{node.TextContent}");
            return;
        }

        var attrs = new List<string>();
        if (!string.IsNullOrEmpty(node.Id)) attrs.Add($"id={node.Id}");
        if (!string.IsNullOrEmpty(node.Role)) attrs.Add($"role={node.Role}");
        if (!string.IsNullOrEmpty(node.AriaLabel)) attrs.Add($"\"{node.AriaLabel}\"");
        if (!string.IsNullOrEmpty(node.Href)) attrs.Add($"href={TruncateUrl(node.Href)}");
        if (!string.IsNullOrEmpty(node.Alt)) attrs.Add($"alt=\"{node.Alt}\"");
        if (!string.IsNullOrEmpty(node.InputType)) attrs.Add($"type={node.InputType}");
        if (!string.IsNullOrEmpty(node.Placeholder)) attrs.Add($"placeholder=\"{node.Placeholder}\"");
        if (!string.IsNullOrEmpty(node.Name)) attrs.Add($"name={node.Name}");

        var attrStr = attrs.Count > 0 ? $" [{string.Join(", ", attrs)}]" : "";
        sb.AppendLine($"{prefix}<{node.Tag}{attrStr}>");

        foreach (var child in node.Children)
            SerializeNode(child, sb, indent + 1);
    }

    private static DomNode CollapseWrappers(DomNode node)
    {
        if (node == null || node.IsText) return node;

        // Recursively collapse children first
        for (var i = 0; i < node.Children.Count; i++)
            node.Children[i] = CollapseWrappers(node.Children[i]);

        // Collapse: if this is a wrapper tag with no semantic meaning and exactly 1 child
        if (WrapperTags.Contains(node.Tag) && !node.HasSemanticMeaning && node.Children.Count == 1)
            return node.Children[0];

        return node;
    }

    private static DomNode RemoveNoiseNodes(DomNode node)
    {
        if (node == null || node.IsText) return node;

        // Check if this node is noise
        if (IsNoiseNode(node))
            return null;

        // Filter children
        var cleanChildren = new List<DomNode>();
        foreach (var child in node.Children)
        {
            var cleaned = RemoveNoiseNodes(child);
            if (cleaned != null)
                cleanChildren.Add(cleaned);
        }
        node.Children = cleanChildren;

        return node;
    }

    private static DomNode RemoveEmptyNodes(DomNode node)
    {
        if (node == null || node.IsText) return node;

        var cleanChildren = new List<DomNode>();
        foreach (var child in node.Children)
        {
            var cleaned = RemoveEmptyNodes(child);
            if (cleaned != null)
                cleanChildren.Add(cleaned);
        }
        node.Children = cleanChildren;

        // Remove if no children and no text content and not interactive
        if (node.Children.Count == 0 && !IsInteractive(node) && string.IsNullOrEmpty(node.Alt))
            return null;

        return node;
    }

    private static bool IsNoiseNode(DomNode node)
    {
        if (node.Id != null)
        {
            foreach (var pattern in NoisyClassPatterns)
                if (node.Id.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
        }

        // iframe without meaningful role
        if (node.Tag == "iframe" && string.IsNullOrEmpty(node.Role) && string.IsNullOrEmpty(node.AriaLabel))
            return true;

        return false;
    }

    private static bool IsInteractive(DomNode node)
    {
        return node.Tag is "a" or "button" or "input" or "textarea" or "select" or "details" or "summary"
            || node.Role is "button" or "link" or "textbox" or "combobox" or "checkbox" or "radio";
    }

    private static string TruncateUrl(string url)
    {
        return url.Length > 80 ? url[..77] + "..." : url;
    }
}
