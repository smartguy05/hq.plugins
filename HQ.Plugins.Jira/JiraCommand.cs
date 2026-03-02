using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Jira.Models;

namespace HQ.Plugins.Jira;

public class JiraCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Jira";
    public override string Description => "Integration with Jira Cloud for project management";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<JiraService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new JiraService(config, Logger);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new
            {
                Success = false,
                Message = $"Error: {e.Message}"
            };
        }
    }
}
