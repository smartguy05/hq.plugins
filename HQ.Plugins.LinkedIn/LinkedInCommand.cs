using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.LinkedIn;

public class LinkedInCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "LinkedIn";
    public override string Description => "LinkedIn messaging, posting, and profile lookup via Relevance AI";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<LinkedInService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config,
        IEnumerable<ToolCall> enumerableToolCalls)
    {
        try
        {
            using var client = new RelevanceAiClient(config.RelevanceAiApiKey, config.RelevanceAiRegion, config.RelevanceAiProjectId);
            var service = new LinkedInService(client, config);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", ex);
            return new { Success = false, Message = $"Error: {ex.Message}" };
        }
    }
}
