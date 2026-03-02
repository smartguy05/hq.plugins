using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Email.Models;

namespace HQ.Plugins.Email;

public class EmailCommand: CommandBase<ServiceRequest,ServiceConfig>
{
    public override string Name => "Email";
    public override string Description => "Send/Read email";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return new EmailService().GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> enumerableToolCalls)
    {
        var emailService = new EmailService(NotificationService);
        return await emailService.ProcessRequest(serviceRequest, config, NotificationService);
    }
}
