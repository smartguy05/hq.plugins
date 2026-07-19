using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.WebSearch.Models;

namespace HQ.Plugins.WebSearch;

public class WebSearchCommand: CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Web Search";
    public override string Description => "A plugin to allow searching the web";
    protected override INotificationService NotificationService { get; set; }

    private HttpMessageHandler _httpMessageHandler;

    // Seam for tests — lets a fake transport be injected without a real HTTP call.
    internal void SetHttpMessageHandler(HttpMessageHandler handler) => _httpMessageHandler = handler;

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(RawServiceRequest, config, NotificationService);
    }

    [Display(Name = "web_search")]
    [Description("Searches the web for information using the configured search engine and returns results.")]
    [Parameters(typeof(WebSearchArgs))]
    public async Task<object> WebSearch(ServiceConfig config, WebSearchArgs request)
    {
        if (string.IsNullOrWhiteSpace(config.WebSearchUrl))
        {
            await Log(LogLevel.Warning, "WebSearchUrl is not configured");
            return new { Success = false, Error = "WebSearchUrl is not configured" };
        }

        using var httpClient = _httpMessageHandler != null
            ? new HttpClient(_httpMessageHandler, disposeHandler: false)
            : new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(config.WebSearchApiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", config.WebSearchApiKey);
        }

        var url = config.WebSearchUrl;
        url += url.Contains('?') ? "&" : "?";
        url += $"q={Uri.EscapeDataString(request.Query)}&limit={request.MaxResults ?? 5}";

        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            await Log(LogLevel.Warning, "Unable to get web search results");
            await Log(LogLevel.Info, await response.Content.ReadAsStringAsync());
            return new
            {
                Success = false
            };
        }

        // Return the raw JSON body wrapped in a single Untrusted envelope. We make no schema
        // assumptions about the search provider's response shape; the host classifies the whole
        // string for prompt-injection before the LLM sees it.
        var raw = await response.Content.ReadAsStringAsync();
        return new
        {
            Success = true,
            Results = new HQ.Models.Safety.Untrusted<string>(raw, "web-search-results", HostOf(config.WebSearchUrl))
        };
    }

    // Provenance source = the search API host; fall back to the raw url string for malformed URLs.
    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }
}
