namespace HQ.Plugins.WebReader;

/// <summary>
/// Fetches a web page and returns its fully-rendered HTML, so the conversion
/// pipeline can work on the same DOM a user would see (JS executed).
/// </summary>
public interface IPageRenderer
{
    Task<RenderedPage> RenderAsync(string url);
}

public record RenderedPage(string Html, string FinalUrl, string Title);
