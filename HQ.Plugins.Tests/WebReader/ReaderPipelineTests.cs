using HQ.Plugins.WebReader;
using HQ.Plugins.WebReader.Models;

namespace HQ.Plugins.Tests.WebReader;

public class ReaderPipelineTests
{
    private static ServiceConfig Config() => new()
    {
        Name = "Web Reader",
        MaxContentLength = 50000,
        IncludeMetadataHeader = true
    };

    // A realistic page: an article wrapped in nav/sidebar/footer chrome that should be stripped.
    private const string ArticleHtml = """
        <html>
          <head><title>Widget Guide</title></head>
          <body>
            <nav><a href="/">Home</a><a href="/about">About Us</a><a href="/contact">Contact</a></nav>
            <header>Mega Site Navigation Banner With Lots Of Links</header>
            <article>
              <h1>The Complete Guide to Widgets</h1>
              <p>Widgets are small components that perform a single useful function. In this guide
              we explore how widgets are designed, why they matter, and how to maintain them over time.
              A good widget is reliable, predictable, and easy to reason about for the engineers who
              depend on it every single day across many different production systems.</p>
              <p>The second principle of widget design is composability. Widgets that compose well let
              teams build larger systems from small, well understood parts. This reduces risk and makes
              the overall architecture far easier to test, reason about, and evolve as requirements
              change over the lifetime of the project and its surrounding ecosystem of tools.</p>
              <p>Finally, observability matters: a widget you cannot measure is a widget you cannot trust
              in production environments where reliability is paramount for the business and its users.</p>
            </article>
            <aside>Related junk links and advertisements that should not appear in output.</aside>
            <footer>Copyright 2026. Privacy Policy. Terms of Service. Cookie banner nonsense.</footer>
            <script>var tracking = 'should not appear';</script>
          </body>
        </html>
        """;

    [Fact]
    public void ToMarkdown_ExtractsMainContent_AndStripsChrome()
    {
        var result = ReaderPipeline.ToMarkdown(ArticleHtml, "https://example.com/guide", "Widget Guide", Config());

        Assert.Contains("Complete Guide to Widgets", result.Markdown);
        Assert.Contains("composability", result.Markdown);

        // Chrome / noise should be gone.
        Assert.DoesNotContain("Privacy Policy", result.Markdown);
        Assert.DoesNotContain("advertisements", result.Markdown);
        Assert.DoesNotContain("tracking", result.Markdown);
    }

    [Fact]
    public void ToMarkdown_IncludesMetadataHeader_WhenEnabled()
    {
        var result = ReaderPipeline.ToMarkdown(ArticleHtml, "https://example.com/guide", "Widget Guide", Config());

        Assert.Contains("URL: https://example.com/guide", result.Markdown);
    }

    [Fact]
    public void ToMarkdown_Truncates_WhenOverMaxLength()
    {
        var config = Config() with { MaxContentLength = 80, IncludeMetadataHeader = false };

        var result = ReaderPipeline.ToMarkdown(ArticleHtml, "https://example.com/guide", "Widget Guide", config);

        Assert.True(result.Truncated);
        Assert.Contains("content truncated", result.Markdown);
    }

    [Fact]
    public void ToMarkdown_FallsBackToBody_WhenNotReadable()
    {
        // Too short / no article structure → Readability won't find a main article.
        const string thin = "<html><body><div>Just a tiny bit of text.</div></body></html>";

        var result = ReaderPipeline.ToMarkdown(thin, "https://example.com/x", "x", Config() with { IncludeMetadataHeader = false });

        Assert.True(result.UsedFallback);
        Assert.Contains("tiny bit of text", result.Markdown);
    }

    [Fact]
    public void ExtractLinks_ResolvesRelativeUrls_AndDedupes()
    {
        const string html = """
            <html><body>
              <a href="/about">About</a>
              <a href="https://other.com/x">External</a>
              <a href="/about">About duplicate</a>
              <a href="#section">Anchor only</a>
              <a href="mailto:a@b.com">Email</a>
            </body></html>
            """;

        var links = ReaderPipeline.ExtractLinks(html, "https://example.com/page", null);

        Assert.Contains(links, l => l.Href == "https://example.com/about");
        Assert.Contains(links, l => l.Href == "https://other.com/x");
        // anchor-only and mailto excluded; /about deduped to one entry.
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public void ExtractLinks_RespectsFilter()
    {
        const string html = """
            <html><body>
              <a href="/blog/post-1">First blog post</a>
              <a href="/shop/item">Buy item</a>
            </body></html>
            """;

        var links = ReaderPipeline.ExtractLinks(html, "https://example.com", "blog");

        Assert.Single(links);
        Assert.Equal("https://example.com/blog/post-1", links[0].Href);
    }

    [Fact]
    public void SearchMarkdown_ReturnsMatchesWithContext()
    {
        const string markdown = "Intro paragraph.\n\nThe quick brown fox jumps over the lazy dog.\n\nClosing thoughts here.";

        var (count, snippets) = ReaderPipeline.SearchMarkdown(markdown, "brown fox", 10);

        Assert.Equal(1, count);
        Assert.Contains("brown fox", snippets);
    }

    [Fact]
    public void SearchMarkdown_NoMatch_ReturnsZero()
    {
        var (count, snippets) = ReaderPipeline.SearchMarkdown("some content", "nonexistent", 50);

        Assert.Equal(0, count);
        Assert.Equal(string.Empty, snippets);
    }
}
