using System.Text.RegularExpressions;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static partial class DiffEngine
{
    public static SnapshotDelta ComputeDiff(PageSnapshot previous, PageSnapshot current)
    {
        if (previous == null || current == null)
            return null;

        var prevLines = ParseLines(previous.AnnotatedYaml);
        var currLines = ParseLines(current.AnnotatedYaml);

        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<ChangedEntry>();
        var unchangedCount = 0;

        // Build lookup by ref ID for previous snapshot
        var prevByRef = new Dictionary<string, DiffLine>();
        foreach (var line in prevLines)
        {
            if (line.RefId != null && !prevByRef.ContainsKey(line.RefId))
                prevByRef[line.RefId] = line;
        }

        // Build lookup by ref ID for current snapshot
        var currByRef = new Dictionary<string, DiffLine>();
        foreach (var line in currLines)
        {
            if (line.RefId != null && !currByRef.ContainsKey(line.RefId))
                currByRef[line.RefId] = line;
        }

        // Find changes and additions
        var matchedPrevRefs = new HashSet<string>();
        foreach (var curr in currLines)
        {
            if (curr.RefId != null && prevByRef.TryGetValue(curr.RefId, out var prev))
            {
                matchedPrevRefs.Add(curr.RefId);
                if (curr.Content != prev.Content)
                {
                    changed.Add(new ChangedEntry
                    {
                        Ref = curr.RefId,
                        Was = prev.Content,
                        Now = curr.Content
                    });
                }
                else
                {
                    unchangedCount++;
                }
            }
            else
            {
                // New line — either new ref or unrefed content change
                added.Add(curr.RawLine);
            }
        }

        // Find removals
        foreach (var prev in prevLines)
        {
            if (prev.RefId != null && !matchedPrevRefs.Contains(prev.RefId))
                removed.Add(prev.RawLine);
        }

        // For unrefed lines, do a simple set diff
        var prevUnrefed = new HashSet<string>(prevLines.Where(l => l.RefId == null).Select(l => l.RawLine));
        var currUnrefed = new HashSet<string>(currLines.Where(l => l.RefId == null).Select(l => l.RawLine));

        // Don't count unrefed lines — they're structural context and would be noisy
        unchangedCount += prevUnrefed.Intersect(currUnrefed).Count();

        return new SnapshotDelta
        {
            Added = added,
            Removed = removed,
            Changed = changed,
            UnchangedCount = unchangedCount
        };
    }

    public static bool IsSignificant(SnapshotDelta delta)
    {
        if (delta == null) return false;
        return delta.Added.Count > 0 || delta.Removed.Count > 0 || delta.Changed.Count > 0;
    }

    private static List<DiffLine> ParseLines(string yaml)
    {
        var result = new List<DiffLine>();
        if (string.IsNullOrEmpty(yaml)) return result;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string refId = null;
            var content = trimmed;

            // Extract [ref=eN] annotation
            var refMatch = RefPattern().Match(trimmed);
            if (refMatch.Success)
            {
                refId = refMatch.Groups[1].Value;
                content = trimmed[..refMatch.Index].TrimEnd();
            }

            result.Add(new DiffLine
            {
                RawLine = rawLine,
                Content = content,
                RefId = refId
            });
        }

        return result;
    }

    [GeneratedRegex(@"\[ref=(e\d+)\]")]
    private static partial Regex RefPattern();
}

public class SnapshotDelta
{
    public List<string> Added { get; init; } = new();
    public List<string> Removed { get; init; } = new();
    public List<ChangedEntry> Changed { get; init; } = new();
    public int UnchangedCount { get; init; }
}

public class ChangedEntry
{
    public string Ref { get; set; }
    public string Was { get; set; }
    public string Now { get; set; }
}

internal class DiffLine
{
    public string RawLine { get; set; }
    public string Content { get; set; }
    public string RefId { get; set; }
}
