using Microsoft.Playwright;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static class AriaSnapshotExtractor
{
    private const int MinUsableLines = 5;

    public static async Task<string> ExtractAsync(IPage page, int timeoutMs = 10000)
    {
        try
        {
            var yaml = await page.Locator("body").AriaSnapshotAsync(new LocatorAriaSnapshotOptions
            {
                Timeout = timeoutMs
            });

            return yaml ?? string.Empty;
        }
        catch (PlaywrightException)
        {
            return string.Empty;
        }
    }

    public static bool IsUsable(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return false;

        var meaningfulLines = 0;
        foreach (var line in yaml.AsSpan().EnumerateLines())
        {
            if (line.Trim().Length > 2)
                meaningfulLines++;

            if (meaningfulLines >= MinUsableLines)
                return true;
        }

        return false;
    }

    public static string Truncate(string yaml, int maxLines)
    {
        if (string.IsNullOrEmpty(yaml))
            return yaml;

        var lines = yaml.Split('\n');
        if (lines.Length <= maxLines)
            return yaml;

        // Find a clean break point — don't cut mid-subtree.
        // Walk backward from maxLines to find a line at indent level 0 or 1.
        var cutAt = maxLines;
        for (var i = maxLines - 1; i >= maxLines - 20 && i >= 0; i--)
        {
            var indent = GetIndentLevel(lines[i]);
            if (indent <= 1)
            {
                cutAt = i + 1;
                break;
            }
        }

        var truncated = string.Join('\n', lines[..cutAt]);
        var remaining = lines.Length - cutAt;
        if (remaining > 0)
            truncated += $"\n... ({remaining} more lines)";

        return truncated;
    }

    public static List<InteractiveElement> ParseInteractiveElements(string yaml)
    {
        var elements = new List<InteractiveElement>();
        if (string.IsNullOrWhiteSpace(yaml))
            return elements;

        var interactiveRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "link", "button", "textbox", "combobox", "checkbox", "radio",
            "slider", "spinbutton", "switch", "menuitem", "menuitemcheckbox",
            "menuitemradio", "tab", "option", "searchbox", "treeitem"
        };

        var lines = yaml.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (!line.StartsWith("- "))
                continue;

            var content = line[2..];
            var spaceIdx = content.IndexOf(' ');
            var quoteIdx = content.IndexOf('"');

            string role;
            if (spaceIdx > 0 && (quoteIdx < 0 || spaceIdx < quoteIdx))
                role = content[..spaceIdx];
            else if (quoteIdx > 0)
                role = content[..quoteIdx];
            else
                role = content.TrimEnd(':');

            if (!interactiveRoles.Contains(role))
                continue;

            // Extract name from quotes
            string name = null;
            if (quoteIdx >= 0)
            {
                var closeQuote = content.IndexOf('"', quoteIdx + 1);
                if (closeQuote > quoteIdx)
                    name = content[(quoteIdx + 1)..closeQuote];
            }

            elements.Add(new InteractiveElement
            {
                Role = role,
                Name = name,
                LineIndex = i,
                IndentLevel = GetIndentLevel(lines[i])
            });
        }

        return elements;
    }

    private static int GetIndentLevel(string line)
    {
        var spaces = 0;
        foreach (var c in line)
        {
            if (c == ' ') spaces++;
            else break;
        }
        return spaces / 2;
    }
}

public class InteractiveElement
{
    public string Role { get; set; }
    public string Name { get; set; }
    public int LineIndex { get; set; }
    public int IndentLevel { get; set; }
}
