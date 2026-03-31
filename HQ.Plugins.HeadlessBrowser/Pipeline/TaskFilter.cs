using System.Text;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static class TaskFilter
{
    private static readonly Dictionary<string, HashSet<string>> KeepRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["form_fill"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "textbox", "combobox", "checkbox", "radio", "button", "form",
            "alert", "status", "label", "switch", "slider", "spinbutton",
            "searchbox", "option", "listbox", "heading"
        },
        ["navigation"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "link", "navigation", "heading", "banner", "menu", "menubar",
            "menuitem", "tab", "tablist", "treeitem", "tree"
        },
        ["data_extraction"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "main", "article", "table", "cell", "row", "rowheader", "columnheader",
            "heading", "list", "listitem", "paragraph", "blockquote", "code",
            "figure", "img", "region", "text"
        },
        ["search"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "search", "searchbox", "textbox", "button", "list", "listitem",
            "heading", "link", "option", "combobox"
        }
    };

    public static string Filter(string yaml, string taskHint)
    {
        if (string.IsNullOrWhiteSpace(yaml) || string.IsNullOrWhiteSpace(taskHint))
            return yaml;

        var hint = taskHint.ToLowerInvariant();
        if (hint == "general" || !KeepRoles.ContainsKey(hint))
            return yaml;

        var allowedRoles = KeepRoles[hint];
        var lines = yaml.Split('\n');
        var sb = new StringBuilder();

        // Track indent levels of kept parents so we include structural context
        var keptIndents = new Stack<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("- "))
            {
                sb.AppendLine(line);
                continue;
            }

            var indent = (line.Length - trimmed.Length) / 2;
            var content = trimmed[2..];

            // Extract role
            var role = ExtractRole(content);

            // Pop indent stack to current level
            while (keptIndents.Count > 0 && keptIndents.Peek() >= indent)
                keptIndents.Pop();

            if (allowedRoles.Contains(role))
            {
                sb.AppendLine(line);
                keptIndents.Push(indent);
            }
            else if (IsContainerRole(role))
            {
                // Check if any descendant should be kept — include container for context
                if (HasRelevantDescendant(lines, i, indent, allowedRoles))
                {
                    sb.AppendLine(line);
                    keptIndents.Push(indent);
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string ExtractRole(string content)
    {
        var spaceIdx = content.IndexOf(' ');
        var colonIdx = content.IndexOf(':');
        var quoteIdx = content.IndexOf('"');

        if (colonIdx > 0 && (spaceIdx < 0 || colonIdx < spaceIdx) && (quoteIdx < 0 || colonIdx < quoteIdx))
            return content[..colonIdx];
        if (spaceIdx > 0 && (quoteIdx < 0 || spaceIdx < quoteIdx))
            return content[..spaceIdx];
        if (quoteIdx > 0)
            return content[..quoteIdx];
        return content.TrimEnd(':');
    }

    private static bool IsContainerRole(string role)
    {
        return role is "main" or "article" or "section" or "region" or "navigation"
            or "banner" or "contentinfo" or "complementary" or "form" or "group"
            or "dialog" or "list" or "menu" or "menubar" or "tablist" or "tree"
            or "toolbar" or "generic";
    }

    private static bool HasRelevantDescendant(string[] lines, int startIdx, int parentIndent, HashSet<string> allowedRoles)
    {
        for (var i = startIdx + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("- "))
                continue;

            var indent = (lines[i].Length - trimmed.Length) / 2;
            if (indent <= parentIndent)
                break; // Past the subtree

            var role = ExtractRole(trimmed[2..]);
            if (allowedRoles.Contains(role))
                return true;
        }

        return false;
    }
}
