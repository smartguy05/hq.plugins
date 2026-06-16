using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.GoogleAnalytics.Models;

namespace HQ.Plugins.GoogleAnalytics;

public class GoogleAnalyticsCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Google Analytics";
    public override string Description => "Integration with Google Analytics 4 for website analytics";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<GoogleAnalyticsService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new GoogleAnalyticsService(Logger);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
