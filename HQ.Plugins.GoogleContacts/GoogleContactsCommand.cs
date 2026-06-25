using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.GoogleContacts.Models;

namespace HQ.Plugins.GoogleContacts;

public class GoogleContactsCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Google Contacts";
    public override string Description => "Manage your personal address book via the Google People API";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<GoogleContactsService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new GoogleContactsService(Logger);
            return await service.ProcessRequest(RawServiceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
