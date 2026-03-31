using System.Text;
using System.Text.RegularExpressions;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static class PageSearcher
{
    private const int ContextLines = 3;

    public static string Search(string annotatedYaml, string query)
    {
        if (string.IsNullOrWhiteSpace(annotatedYaml) || string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var lines = annotatedYaml.Split('\n');
        var matchIndices = new HashSet<int>();

        // Find lines that match the query (case-insensitive)
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                matchIndices.Add(i);
        }

        if (matchIndices.Count == 0)
            return $"No matches found for: {query}";

        // Build result with context lines
        var sb = new StringBuilder();
        sb.AppendLine($"Found {matchIndices.Count} match(es) for \"{query}\":");
        sb.AppendLine();

        var emittedLines = new HashSet<int>();
        var lastEmitted = -2;

        foreach (var matchIdx in matchIndices.OrderBy(i => i))
        {
            var start = Math.Max(0, matchIdx - ContextLines);
            var end = Math.Min(lines.Length - 1, matchIdx + ContextLines);

            if (start > lastEmitted + 1 && lastEmitted >= 0)
                sb.AppendLine("  ...");

            for (var i = start; i <= end; i++)
            {
                if (emittedLines.Contains(i))
                    continue;

                var prefix = i == matchIdx ? ">> " : "   ";
                sb.AppendLine($"{prefix}{lines[i]}");
                emittedLines.Add(i);
                lastEmitted = i;
            }
        }

        return sb.ToString().TrimEnd();
    }
}
