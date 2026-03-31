namespace HQ.Plugins.HeadlessBrowser.Models;

public record ElementRef
{
    public string RefId { get; init; }
    public string Role { get; init; }
    public string Name { get; init; }
    public int LineIndex { get; init; }
    public int IndentLevel { get; init; }
}
