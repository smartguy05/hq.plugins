using System.Text.Json;
using HQ.Models.Safety;
using HQ.Plugins.WebReader;
using HQ.Plugins.WebReader.Models;

namespace HQ.Plugins.Tests.WebReader;

/// <summary>
/// Verifies that WebReader wraps open-web page content (highest prompt-injection risk) in
/// <see cref="Untrusted{T}"/> envelopes with the page host as the source. Classification itself
/// is the host's job — these tests only check that the plugin emits the right provenance markers.
/// </summary>
public class WebReaderProvenanceTests
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

    private const string PageUrl = "https://news.example.com/article";
    private const string PageHost = "news.example.com";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new UntrustedJsonConverterFactory() }
    };

    private static ServiceConfig Config() => new() { Name = "Web Reader", MaxContentLength = 50000 };

    private static WebReaderCommand CommandWith(string html)
    {
        var cmd = new WebReaderCommand();
        cmd.SetRenderer(new FakeRenderer(html, PageUrl, "Sample"));
        return cmd;
    }

    private static JsonElement ToJson(object o) =>
        JsonDocument.Parse(JsonSerializer.Serialize(o, JsonOpts)).RootElement.Clone();

    [Fact]
    public async Task ReadPage_WrapsMarkdownAsUntrusted()
    {
        var cmd = CommandWith(PageHtml);

        var result = await cmd.ReadPage(Config(), new ReadPageArgs { Url = PageUrl });

        var markdown = ToJson(result).GetProperty("Markdown");
        Assert.True(markdown.GetProperty("__untrusted").GetBoolean());
        Assert.Equal("web-page-content", markdown.GetProperty("provenance").GetString());
        Assert.Equal(PageHost, markdown.GetProperty("source").GetString());
        Assert.Contains("Sample Article", markdown.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ExtractLinks_WrapsLinksAsUntrusted()
    {
        var cmd = CommandWith(PageHtml);

        var result = await cmd.ExtractLinks(Config(), new ExtractLinksArgs { Url = PageUrl });

        var links = ToJson(result).GetProperty("Links");
        Assert.True(links.GetProperty("__untrusted").GetBoolean());
        Assert.Equal("web-page-links", links.GetProperty("provenance").GetString());
        Assert.Equal(PageHost, links.GetProperty("source").GetString());
    }

    [Fact]
    public async Task SearchPage_WrapsSnippetsAsUntrusted()
    {
        var cmd = CommandWith(PageHtml);

        var result = await cmd.SearchPage(Config(),
            new SearchPageArgs { Url = PageUrl, Query = "second paragraph" });

        var snippets = ToJson(result).GetProperty("Snippets");
        Assert.True(snippets.GetProperty("__untrusted").GetBoolean());
        Assert.Equal("web-page-snippets", snippets.GetProperty("provenance").GetString());
        Assert.Equal(PageHost, snippets.GetProperty("source").GetString());
    }
}
