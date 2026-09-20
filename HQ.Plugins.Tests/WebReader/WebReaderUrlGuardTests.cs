using System.Text.Json;
using HQ.Plugins.WebReader;
using HQ.Plugins.WebReader.Models;

namespace HQ.Plugins.Tests.WebReader;

/// <summary>
/// WP6A-1 (Critical): WebReader rendered whatever URL it was given with no scheme/host check,
/// so a prompt-injected "read_page file:///app/dpkeys/" (or an internal http:// SSRF target) went
/// straight to Playwright. These tests exercise the guard through the injected
/// <see cref="IPageRenderer"/> seam — no real browser is launched, per the cluster ruling that
/// non-browser assertions stay unit tests and any real-Chromium coverage is
/// [Trait("Category","Integration")].
/// </summary>
public class WebReaderUrlGuardTests
{
    private sealed class SpyRenderer : IPageRenderer
    {
        public string RequestedUrl { get; private set; }
        public int CallCount { get; private set; }

        public Task<RenderedPage> RenderAsync(string requestedUrl)
        {
            RequestedUrl = requestedUrl;
            CallCount++;
            return Task.FromResult(new RenderedPage("<html><body>ok</body></html>", requestedUrl, "OK"));
        }
    }

    private static ServiceConfig Config() => new() { Name = "Web Reader", MaxContentLength = 50000 };

    private static WebReaderCommand CommandWith(SpyRenderer renderer)
    {
        var cmd = new WebReaderCommand();
        cmd.SetRenderer(renderer);
        return cmd;
    }

    public static IEnumerable<object[]> BlockedUrls => new[]
    {
        new object[] { "file:///app/dpkeys/" },
        new object[] { "file:///etc/passwd" },
        new object[] { "http://127.0.0.1:8080/" },
        new object[] { "http://169.254.169.254/latest/meta-data/" },
        new object[] { "http://10.0.0.5/" },
        new object[] { "http://hq-postgres:5432/" },
        new object[] { "http://hq-chromadb:8000/api/v1/collections" },
        new object[] { "http://localhost/" }
    };

    [Theory]
    [MemberData(nameof(BlockedUrls))]
    public async Task ReadPage_WithDisallowedTarget_BlocksAndNeverRenders(string url)
    {
        var renderer = new SpyRenderer();
        var cmd = CommandWith(renderer);

        var result = await cmd.ReadPage(Config(), new ReadPageArgs { Url = url });

        Assert.Contains("\"Success\":false", JsonSerializer.Serialize(result));
        Assert.Equal(0, renderer.CallCount);
    }

    [Theory]
    [MemberData(nameof(BlockedUrls))]
    public async Task ExtractLinks_WithDisallowedTarget_BlocksAndNeverRenders(string url)
    {
        var renderer = new SpyRenderer();
        var cmd = CommandWith(renderer);

        var result = await cmd.ExtractLinks(Config(), new ExtractLinksArgs { Url = url });

        Assert.Contains("\"Success\":false", JsonSerializer.Serialize(result));
        Assert.Equal(0, renderer.CallCount);
    }

    [Theory]
    [MemberData(nameof(BlockedUrls))]
    public async Task SearchPage_WithDisallowedTarget_BlocksAndNeverRenders(string url)
    {
        var renderer = new SpyRenderer();
        var cmd = CommandWith(renderer);

        var result = await cmd.SearchPage(Config(), new SearchPageArgs { Url = url, Query = "x" });

        Assert.Contains("\"Success\":false", JsonSerializer.Serialize(result));
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task ReadPage_WithOrdinaryHttpsUrl_IsAllowedThroughToTheRenderer()
    {
        var renderer = new SpyRenderer();
        var cmd = CommandWith(renderer);

        var result = await cmd.ReadPage(Config(), new ReadPageArgs { Url = "https://example.com/article" });

        Assert.Contains("\"Success\":true", JsonSerializer.Serialize(result));
        Assert.Equal(1, renderer.CallCount);
        Assert.Equal("https://example.com/article", renderer.RequestedUrl);
    }

    [Fact]
    public async Task ReadPage_ConsultsTheInjectedUrlValidatorSeam()
    {
        var renderer = new SpyRenderer();
        var cmd = CommandWith(renderer);
        string seenUrl = null;
        cmd.UrlValidator = url =>
        {
            seenUrl = url;
            return (true, null);
        };

        await cmd.ReadPage(Config(), new ReadPageArgs { Url = "https://example.com/article" });

        Assert.Equal("https://example.com/article", seenUrl);
    }

    [Fact]
    public async Task ReadPage_WhenInjectedValidatorRejects_NeverCallsRendererEvenForAnAllowedLookingUrl()
    {
        var renderer = new SpyRenderer();
        var cmd = CommandWith(renderer);
        cmd.UrlValidator = _ => (false, "blocked by test seam");

        var result = await cmd.ReadPage(Config(), new ReadPageArgs { Url = "https://example.com/article" });

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"Success\":false", json);
        Assert.Contains("blocked by test seam", json);
        Assert.Equal(0, renderer.CallCount);
    }

    [Theory]
    [InlineData("file:///app/dpkeys/", false)]
    [InlineData("http://hq-postgres:5432/", false)]
    [InlineData("http://127.0.0.1/", false)]
    [InlineData("https://example.com/", true)]
    public void PlaywrightRenderer_IsRequestNavigable_MatchesUrlGuardForRouteFilter(string url, bool expected)
    {
        Assert.Equal(expected, PlaywrightRenderer.IsRequestNavigable(url));
    }
}
