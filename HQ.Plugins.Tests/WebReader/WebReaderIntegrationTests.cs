using HQ.Plugins.WebReader;
using HQ.Plugins.WebReader.Models;

namespace HQ.Plugins.Tests.WebReader;

// Live tests: require network access and an installed Chromium. Excluded from the
// default unit run via: dotnet test --filter "Category!=Integration".
[Trait("Category", "Integration")]
public class WebReaderIntegrationTests
{
    private static ServiceConfig Config() => new()
    {
        Name = "Web Reader",
        MaxContentLength = 50000,
        WaitForNetworkIdle = true,
        IncludeMetadataHeader = true
    };

    [Fact]
    public async Task ReadPage_RealArticle_ReturnsMarkdown_AndIsFarSmallerThanHtml()
    {
        var renderer = new PlaywrightRenderer(Config());
        await using (renderer as IAsyncDisposable)
        {
            var rendered = await renderer.RenderAsync("https://en.wikipedia.org/wiki/Markdown");
            var result = ReaderPipeline.ToMarkdown(rendered.Html, rendered.FinalUrl, rendered.Title, Config());

            Assert.False(string.IsNullOrWhiteSpace(result.Markdown));
            Assert.Contains("Markdown", result.Markdown);

            // The whole point: markdown is dramatically smaller than the raw HTML.
            var ratio = (double)rendered.Html.Length / Math.Max(1, result.Markdown.Length);
            Console.WriteLine($"[WebReader] HTML={rendered.Html.Length} chars, Markdown={result.Markdown.Length} chars, reduction={ratio:F1}x");
            Assert.True(ratio > 2.0, $"Expected markdown to be much smaller than HTML, got {ratio:F1}x");
        }
    }
}
