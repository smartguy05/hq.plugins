using System.Text;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static class OutlineBuilder
{
    private static readonly HashSet<string> OutlineRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "heading", "navigation", "main", "banner", "contentinfo", "complementary",
        "form", "search", "region", "landmark", "menu", "menubar", "tablist",
        "link", "button", "textbox", "combobox", "checkbox", "radio",
        "searchbox", "switch", "tab", "img", "table"
    };

    public static string Build(string annotatedYaml)
    {
        if (string.IsNullOrWhiteSpace(annotatedYaml))
            return string.Empty;

        var sb = new StringBuilder();
        var lines = annotatedYaml.Split('\n');

        // Track which parent lines we need to include for context
        var parentStack = new List<(int indent, string line)>();

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimStart();
            if (!trimmed.StartsWith("- "))
                continue;

            var indent = (rawLine.Length - trimmed.Length) / 2;
            var content = trimmed[2..];

            // Extract role
            var spaceIdx = content.IndexOf(' ');
            var colonIdx = content.IndexOf(':');
            string role;
            if (colonIdx > 0 && (spaceIdx < 0 || colonIdx < spaceIdx))
                role = content[..colonIdx];
            else if (spaceIdx > 0)
                role = content[..spaceIdx];
            else
                role = content.TrimEnd(':');

            // Maintain parent stack
            while (parentStack.Count > 0 && parentStack[^1].indent >= indent)
                parentStack.RemoveAt(parentStack.Count - 1);

            if (OutlineRoles.Contains(role))
            {
                // Emit any parents not yet emitted
                foreach (var parent in parentStack)
                    sb.AppendLine(parent.line);
                parentStack.Clear();

                sb.AppendLine(rawLine);
            }
            else
            {
                // Track as potential parent for context
                parentStack.Add((indent, rawLine));
            }
        }

        return sb.ToString().TrimEnd();
    }
}
