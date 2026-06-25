using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Perplexity.Models;
using HQ.Services.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace HQ.Plugins.Perplexity;

public class PerplexityCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Perplexity Research";
    public override string Description => "Research the web with Perplexity's Sonar models, returning cited answers.";
    protected override INotificationService NotificationService { get; set; }

    // Deep research uses the async job API: poll every PollInterval, give up after MaxWait.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(30);

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
                TimeSpan.FromMinutes(2));

            return new { Success = true, Answer = result.Answer, Citations = result.Citations };
        }
        catch (Exception e)
        {
            await Log(LogLevel.Warning, $"Perplexity search failed: {e.Message}");
            return new { Success = false, Error = e.Message };
        }
    }

    [Display(Name = "perplexity_deep_research")]
    [Description("Run an exhaustive multi-step Perplexity deep-research job (sonar-deep-research). Takes several minutes; the cited results are delivered back into this conversation when ready. Use for thorough research, not quick lookups.")]
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

        // Without a conversation id we cannot deliver the result back later, so submit-and-poll inline
        // and return the answer in this turn instead.
        if (string.IsNullOrWhiteSpace(serviceRequest.ConversationId))
        {
            try
            {
                var result = await PerplexityClient.RunDeepResearchAsync(
                    config.PerplexityApiKey, serviceRequest.Query, serviceRequest.Recency,
                    domainFilters, config.MaxTokens, PollInterval, MaxWait);
                return new { Success = true, Answer = result.Answer, Citations = result.Citations };
            }
            catch (Exception e)
            {
                await Log(LogLevel.Warning, $"Deep research failed: {e.Message}");
                return new { Success = false, Error = e.Message };
            }
        }

        // Capture everything the background task needs before returning.
        var apiKey = config.PerplexityApiKey;
        var query = serviceRequest.Query;
        var recency = serviceRequest.Recency;
        var maxTokens = config.MaxTokens;
        var conversationId = serviceRequest.ConversationId;
        var agentId = config.AgentId;
        var routeService = !string.IsNullOrWhiteSpace(config.AiPlugin)
            ? config.AiPlugin
            : serviceRequest.RequestingService;

        _ = Task.Run(async () =>
        {
            string deliverable;
            try
            {
                var result = await PerplexityClient.RunDeepResearchAsync(
                    apiKey, query, recency, domainFilters, maxTokens, PollInterval, MaxWait);
                deliverable = FormatDeliverable(query, result);
            }
            catch (Exception e)
            {
                await Log(LogLevel.Error, $"Deep research background job failed: {e.Message}");
                deliverable = $"[Deep research for: {query}]\n\nThe research job failed: {e.Message}";
            }

            try
            {
                await DeliverToConversation(routeService, conversationId, agentId, deliverable);
            }
            catch (Exception e)
            {
                await Log(LogLevel.Error, $"Failed to deliver deep research results to conversation {conversationId}: {e.Message}");
            }
        });

        return new
        {
            Success = true,
            Status = "started",
            Message = "Deep research underway; results will be posted back to this conversation when ready."
        };
    }

    /// <summary>
    /// Posts the deep-research result back into the originating conversation as a new turn, using the
    /// same orchestrator entry point that inbound channel messages use.
    /// </summary>
    private static async Task DeliverToConversation(string routeService, string conversationId, Guid? agentId, string content)
    {
        var serviceRequest = JsonSerializer.Serialize(new
        {
            SystemPrompt = (string)null,
            UserPrompt = content,
            ConversationId = conversationId,
            Photo = (string)null
        });

        var request = new OrchestratorRequest
        {
            Service = routeService,
            ServiceRequest = serviceRequest,
            AgentId = agentId
        };

        using var scope = ServiceResolver.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
        await orchestrator.ProcessRequest(request);
    }

    private static string FormatDeliverable(string query, PerplexityResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[Deep research results for: {query}]");
        sb.AppendLine();
        sb.AppendLine(result.Answer);

        if (result.Citations is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Sources:");
            foreach (var citation in result.Citations)
            {
                sb.AppendLine($"- {citation}");
            }
        }

        return sb.ToString();
    }

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
