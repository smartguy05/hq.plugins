using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Plaid.Models;

namespace HQ.Plugins.Plaid;

public class PlaidCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Plaid";
    public override string Description => "Read personal bank accounts, balances and transactions via Plaid";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<PlaidService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new PlaidService(Logger);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
