using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Perplexity.Models;

namespace HQ.Plugins.Perplexity;

public class PerplexityCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Perplexity Research";
    public override string Description => "Research the web with Perplexity's Sonar models, returning cited answers.";
    protected override INotificationService NotificationService { get; set; }

    // Deep research uses the async job API: poll every PollInterval, give up after MaxWait.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(30);

    private System.Net.Http.HttpMessageHandler _httpMessageHandler;

    // Seam for tests — lets a fake transport be injected without a real HTTP call.
    internal void SetHttpMessageHandler(System.Net.Http.HttpMessageHandler handler) => _httpMessageHandler = handler;

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(RawServiceRequest, config, NotificationService);
    }

    [Display(Name = "perplexity_search")]
    [Description("Search the web with Perplexity and return a synthesized, cited answer. Fast (seconds). Use for quick factual lookups and current information.")]
    [Parameters(typeof(PerplexitySearchArgs))]
    public async Task<object> PerplexitySearch(ServiceConfig config, PerplexitySearchArgs serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(config.PerplexityApiKey))
        {
            await Log(LogLevel.Warning, "PerplexityApiKey is not configured");
            return new { Success = false, Error = "PerplexityApiKey is not configured" };
        }

        if (string.IsNullOrWhiteSpace(serviceRequest.Query))
        {
            return new { Success = false, Error = "query is required" };
        }

        var model = !string.IsNullOrWhiteSpace(serviceRequest.Model)
            ? serviceRequest.Model
            : !string.IsNullOrWhiteSpace(config.DefaultSearchModel) ? config.DefaultSearchModel : "sonar-pro";

        try
        {
            var result = await PerplexityClient.ResearchAsync(
                config.PerplexityApiKey,
                model,
                serviceRequest.Query,
                serviceRequest.Recency,
                MergeDomainFilters(config, serviceRequest.DomainFilters),
                config.MaxTokens,
                TimeSpan.FromMinutes(2),
                _httpMessageHandler);

            return new
            {
                Success = true,
                Answer = AsUntrusted(result.Answer, "perplexity-answer", "perplexity.ai"),
                Citations = AsUntrusted(JoinCitations(result.Citations), "perplexity-citations", "perplexity.ai")
            };
        }
        catch (Exception e)
        {
            await Log(LogLevel.Warning, $"Perplexity search failed: {e.Message}");
            return new { Success = false, Error = e.Message };
        }
    }

    // WP6B-15: this tool used to accept a [ConversationId]/[RequestingService] tool arg and, when
    // present, deliver the result asynchronously into that conversation on a background thread.
    // [Injected] only hides a property from the LLM-visible schema — DeserializeArgs still binds a
    // same-named JSON key regardless, so a prompt-injected model could set its own conversationId
    // and redirect delivery to an arbitrary (possibly cross-tenant) conversation, on a thread with
    // no tenant context established. There is no host-trusted per-call conversation id available
    // to a plugin today (unlike organizationId, which PluginService force-injects before Execute).
    // Until that host-side companion (force-inject + tenant-scoped delivery) exists, this tool
    // always runs synchronously and returns the answer directly — see
    // PerplexityDeepResearchArgs for the removed properties.
    [Display(Name = "perplexity_deep_research")]
    [Description("Run an exhaustive multi-step Perplexity deep-research job (sonar-deep-research). Takes several minutes and returns the cited answer directly. Use for thorough research, not quick lookups.")]
    [Parameters(typeof(PerplexityDeepResearchArgs))]
    public async Task<object> PerplexityDeepResearch(ServiceConfig config, PerplexityDeepResearchArgs serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(config.PerplexityApiKey))
        {
            await Log(LogLevel.Warning, "PerplexityApiKey is not configured");
            return new { Success = false, Error = "PerplexityApiKey is not configured" };
        }

        if (string.IsNullOrWhiteSpace(serviceRequest.Query))
        {
            return new { Success = false, Error = "query is required" };
        }

        var domainFilters = MergeDomainFilters(config, serviceRequest.DomainFilters);

        try
        {
            var result = await PerplexityClient.RunDeepResearchAsync(
                config.PerplexityApiKey, serviceRequest.Query, serviceRequest.Recency,
                domainFilters, config.MaxTokens, PollInterval, MaxWait);
            return new
            {
                Success = true,
                Answer = AsUntrusted(result.Answer, "perplexity-answer", "perplexity.ai"),
                Citations = AsUntrusted(JoinCitations(result.Citations), "perplexity-citations", "perplexity.ai")
            };
        }
        catch (Exception e)
        {
            await Log(LogLevel.Warning, $"Deep research failed: {e.Message}");
            return new { Success = false, Error = e.Message };
        }
    }

    // Wrap externally-sourced answer/citations so the host can classify them for prompt-injection.
    private static object AsUntrusted(string content, string provenance, string source) =>
        string.IsNullOrEmpty(content) ? content : new HQ.Models.Safety.Untrusted<string>(content, provenance, source);

    private static string JoinCitations(List<string> citations) =>
        citations is { Count: > 0 } ? string.Join("\n", citations) : null;

    private static List<string> MergeDomainFilters(ServiceConfig config, List<string> requestDomainFilters)
    {
        var merged = new List<string>();
        if (config.DefaultDomainFilters is { Count: > 0 }) merged.AddRange(config.DefaultDomainFilters);
        if (requestDomainFilters is { Count: > 0 }) merged.AddRange(requestDomainFilters);
        return merged
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct()
            .ToList();
    }
}
