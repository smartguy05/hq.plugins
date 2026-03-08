using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.HeadlessBrowser.Models;

namespace HQ.Plugins.HeadlessBrowser;

public class HeadlessBrowserCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    private static BrowserClient _browserClient;

    public override string Name => "HeadlessBrowser";
    public override string Description => "Headless browser automation for navigating pages, reading content, filling forms, clicking elements, and taking screenshots";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<HeadlessBrowserService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            _browserClient ??= new BrowserClient(config);
            var service = new HeadlessBrowserService(_browserClient, config, Logger);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
