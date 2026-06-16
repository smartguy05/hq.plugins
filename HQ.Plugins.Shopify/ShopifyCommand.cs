using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Shopify.Models;

namespace HQ.Plugins.Shopify;

public class ShopifyCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Shopify";
    public override string Description => "Integration with Shopify for e-commerce store management";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<ShopifyService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new ShopifyService(NotificationService, Logger);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
