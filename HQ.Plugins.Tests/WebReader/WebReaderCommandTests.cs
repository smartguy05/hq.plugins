using System.Text.Json;
using HQ.Plugins.WebReader;
using HQ.Plugins.WebReader.Models;

namespace HQ.Plugins.Tests.WebReader;

public class WebReaderCommandTests
{
    private sealed class FakeRenderer(string html, string url, string title) : IPageRenderer
    {
        public Task<RenderedPage> RenderAsync(string requestedUrl) =>
            Task.FromResult(new RenderedPage(html, url, title));
    }

    private const string PageHtml = """
        <html><head><title>Sample</title></head>
        <body>
          <nav><a href="/home">Home</a></nav>
          <article>
            <h1>Sample Article</h1>
            <p>This is a reasonably long article body about testing the web reader plugin.
            It must contain enough readable prose for the readability extractor to treat it as
            the main content of the page rather than navigation chrome or boilerplate text.</p>
            <p>A second paragraph adds more substance so the extracted markdown is meaningful and
            the conversion pipeline has real content to work with during the unit test run.</p>
            <a href="/related">Related article</a>
          </article>
          <footer>Footer noise. Privacy Policy.</footer>
        </body></html>
        """;

    private static ServiceConfig Config() => new() { Name = "Web Reader", MaxContentLength = 50000 };

    private static WebReaderCommand CommandWith(string html, string url = "https://example.com/sample", string title = "Sample")
    {
        var cmd = new WebReaderCommand();
        cmd.SetRenderer(new FakeRenderer(html, url, title));
        return cmd;
    }

    private static string Json(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public async Task ReadPage_ReturnsMarkdown_Success()
    {
        var cmd = CommandWith(PageHtml);
        var request = new ServiceRequest { Url = "https://example.com/sample" };

        var result = await cmd.ReadPage(Config(), request);
        var json = Json(result);

        Assert.Contains("\"Success\":true", json);
        Assert.Contains("Sample Article", json);
        Assert.DoesNotContain("Privacy Policy", json);
    }

    [Fact]
    public async Task ReadPage_MissingUrl_ReturnsError()
    {
        var cmd = CommandWith(PageHtml);
        var result = await cmd.ReadPage(Config(), new ServiceRequest { Url = "" });

        Assert.Contains("\"Success\":false", Json(result));
    }

    [Fact]
    public async Task ExtractLinks_ReturnsMarkdownList()
    {
        var cmd = CommandWith(PageHtml);
        var request = new ServiceRequest { Url = "https://example.com/sample" };

        var result = await cmd.ExtractLinks(Config(), request);
        var json = Json(result);

        Assert.Contains("\"Success\":true", json);
        Assert.Contains("https://example.com/related", json);
        Assert.Contains("https://example.com/home", json);
    }

    [Fact]
    public async Task SearchPage_ReturnsOnlyMatches()
    {
        var cmd = CommandWith(PageHtml);
        var request = new ServiceRequest { Url = "https://example.com/sample", Query = "second paragraph", ContextChars = 40 };

        var result = await cmd.SearchPage(Config(), request);
        var json = Json(result);

        Assert.Contains("\"Success\":true", json);
        Assert.Contains("\"MatchCount\":1", json);
        Assert.Contains("second paragraph", json);
    }

    [Fact]
    public async Task SearchPage_MissingQuery_ReturnsError()
    {
        var cmd = CommandWith(PageHtml);
        var result = await cmd.SearchPage(Config(), new ServiceRequest { Url = "https://example.com/sample", Query = "" });

        Assert.Contains("\"Success\":false", Json(result));
    }
}
