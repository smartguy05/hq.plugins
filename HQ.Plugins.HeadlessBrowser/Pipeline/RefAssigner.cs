using HQ.Plugins.HeadlessBrowser.Models;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static class RefAssigner
{
    private const int MaxRefs = 500;

    public static PageSnapshot Assign(string yaml, string url)
    {
        var elements = AriaSnapshotExtractor.ParseInteractiveElements(yaml);
        var refMap = new Dictionary<string, ElementRef>();
        var lines = yaml.Split('\n');
        var refCounter = 1;

        foreach (var element in elements)
        {
            if (refCounter > MaxRefs)
                break;

            var refId = $"e{refCounter}";
            refMap[refId] = new ElementRef
            {
                RefId = refId,
                Role = element.Role,
                Name = element.Name,
                LineIndex = element.LineIndex,
                IndentLevel = element.IndentLevel
            };

            // Annotate the YAML line with the ref ID
            var line = lines[element.LineIndex];
            lines[element.LineIndex] = $"{line.TrimEnd()} [ref={refId}]";
            refCounter++;
        }

        return new PageSnapshot
        {
            Yaml = yaml,
            AnnotatedYaml = string.Join('\n', lines),
            Url = url,
            Timestamp = DateTime.UtcNow,
            RefMap = refMap
        };
    }
}
