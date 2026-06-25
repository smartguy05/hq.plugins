using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Mailchimp.Models;

namespace HQ.Plugins.Mailchimp;

public class MailchimpCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Mailchimp";
    public override string Description => "Integration with Mailchimp for email marketing";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<MailchimpService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new MailchimpService(NotificationService, Logger);
            return await service.ProcessRequest(RawServiceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
