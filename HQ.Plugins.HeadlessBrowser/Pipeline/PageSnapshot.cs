using HQ.Plugins.HeadlessBrowser.Models;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public class PageSnapshot
{
    public string Yaml { get; init; }
    public string AnnotatedYaml { get; init; }
    public string Url { get; init; }
    public DateTime Timestamp { get; init; }
    public IReadOnlyDictionary<string, ElementRef> RefMap { get; init; }
}
