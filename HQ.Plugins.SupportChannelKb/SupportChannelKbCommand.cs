using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.SupportChannelKb.Models;

namespace HQ.Plugins.SupportChannelKb;

public class SupportChannelKbCommand : CommandBase<ServiceRequest,ServiceConfig>
{
    public override string Name => "HQ.Plugins.SupportChannelKb";
    public override string Description => "Plugin for interfacing with the Support Channel Knowledge Base API";
    protected override INotificationService NotificationService { get; set; }
    
    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<SupportChannelKbService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> enumerableToolCalls)
    {
        try
        {
            var service = new SupportChannelKbService(config);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", ex);
            return new
            {
                Success = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
}