using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.DocuSign.Models;

namespace HQ.Plugins.DocuSign;

public class DocuSignCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "DocuSign";
    public override string Description => "Integration with DocuSign for e-signature";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<DocuSignService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new DocuSignService(NotificationService, Logger);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
