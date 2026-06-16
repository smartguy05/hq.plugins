using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Asana.Models;

namespace HQ.Plugins.Asana;

public class AsanaCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Asana";
    public override string Description => "Integration with Asana for task and project management";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<AsanaService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new AsanaService(config, Logger);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
