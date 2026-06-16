using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.WebReader.Models;
using Microsoft.Playwright;

namespace HQ.Plugins.WebReader;

public class WebReaderCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    private IPageRenderer _renderer;

    public override string Name => "Web Reader";

    public override string Description =>
        "Reads web pages as clean, token-efficient markdown. Renders the page (JS included), strips nav/ads/footers, and returns the main content as markdown. Also lists links and searches page text.";

    protected override INotificationService NotificationService { get; set; }

    // Seam for tests — lets a fake renderer be injected without a real browser.
    public void SetRenderer(IPageRenderer renderer) => _renderer = renderer;

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    public override async Task<object> Initialize(string configString, LogDelegate logFunction, INotificationService notificationService)
    {
        await base.Initialize(configString, logFunction, notificationService);

        try
        {
            var exitCode = Program.Main(["install", "chromium"]);
            if (exitCode != 0)
                await logFunction(LogLevel.Warning, $"Playwright browser install returned exit code {exitCode}");
        }
        catch (Exception ex)
        {
            await logFunction(LogLevel.Warning, $"Failed to install Playwright browsers: {ex.Message}");
        }

        return null;
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(serviceRequest, config, NotificationService);
    }

    [Display(Name = "read_page")]
    [Description("Fetches a web page, strips navigation/ads/sidebars/footers, and returns the main content as clean markdown. Use this to read articles, docs, and pages efficiently instead of raw HTML.")]
    [Parameters("""{"type":"object","properties":{"url":{"type":"string","description":"The URL of the page to read"},"maxLength":{"type":"integer","description":"Optional cap on the number of markdown characters returned. Defaults to the configured maximum."}},"required":["url"]}""")]
    public async Task<object> ReadPage(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(serviceRequest.Url))
            return new { Success = false, Message = "A 'url' is required." };

        return await WithRender(config, serviceRequest.Url, rendered =>
        {
            var effectiveConfig = serviceRequest.MaxLength is > 0
                ? config with { MaxContentLength = serviceRequest.MaxLength.Value }
                : config;

            var result = ReaderPipeline.ToMarkdown(rendered.Html, rendered.FinalUrl, rendered.Title, effectiveConfig);

            return new
            {
                Success = true,
                Url = rendered.FinalUrl,
                result.Title,
                result.Byline,
                result.SiteName,
                result.Length,
                result.Truncated,
                ExtractionFallback = result.UsedFallback,
                result.Markdown
            };
        });
    }

    [Display(Name = "extract_links")]
    [Description("Returns the links on a page as a markdown list of [text](absolute-url), optionally filtered by a substring. Useful for navigating from a page to related content.")]
    [Parameters("""{"type":"object","properties":{"url":{"type":"string","description":"The URL of the page to extract links from"},"filter":{"type":"string","description":"Optional substring; only links whose text or URL contains it are returned (case-insensitive)."}},"required":["url"]}""")]
    public async Task<object> ExtractLinks(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(serviceRequest.Url))
            return new { Success = false, Message = "A 'url' is required." };

        return await WithRender(config, serviceRequest.Url, rendered =>
        {
            var links = ReaderPipeline.ExtractLinks(rendered.Html, rendered.FinalUrl, serviceRequest.Filter);
            var markdown = string.Join("\n", links.Select(l => $"- [{l.Text}]({l.Href})"));

            return new
            {
                Success = true,
                Url = rendered.FinalUrl,
                Count = links.Count,
                Links = markdown
            };
        });
    }

    [Display(Name = "search_page")]
    [Description("Fetches a page and returns only the markdown sections containing the query (with surrounding context). Use this to find specific information on a large page without reading the whole thing.")]
    [Parameters("""{"type":"object","properties":{"url":{"type":"string","description":"The URL of the page to search"},"query":{"type":"string","description":"Text to find within the page (case-insensitive)"},"contextChars":{"type":"integer","description":"Characters of context to include around each match. Defaults to 200."}},"required":["url","query"]}""")]
    public async Task<object> SearchPage(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(serviceRequest.Url))
            return new { Success = false, Message = "A 'url' is required." };
        if (string.IsNullOrWhiteSpace(serviceRequest.Query))
            return new { Success = false, Message = "A 'query' is required." };

        return await WithRender(config, serviceRequest.Url, rendered =>
        {
            var markdown = ReaderPipeline.FullMarkdown(rendered.Html);
            var (count, snippets) = ReaderPipeline.SearchMarkdown(markdown, serviceRequest.Query, serviceRequest.ContextChars ?? 200);

            return new
            {
                Success = true,
                Url = rendered.FinalUrl,
                serviceRequest.Query,
                MatchCount = count,
                Snippets = snippets
            };
        });
    }

    private async Task<object> WithRender(ServiceConfig config, string url, Func<RenderedPage, object> project)
    {
        try
        {
            _renderer ??= new PlaywrightRenderer(config);
            var rendered = await _renderer.RenderAsync(url);
            return project(rendered);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
        {
            await Log(LogLevel.Warning, "Playwright browsers are not installed");
            return new
            {
                Success = false,
                Message = "Playwright browsers are not installed. Run 'pwsh bin/Debug/net9.0/Plugins/playwright.ps1 install chromium' to install."
            };
        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, $"Error reading page '{url}'", ex);
            return new { Success = false, Message = $"Error: {ex.Message}" };
        }
    }
}
