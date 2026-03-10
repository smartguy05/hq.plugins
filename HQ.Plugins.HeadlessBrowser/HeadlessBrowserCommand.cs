using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.HeadlessBrowser.Models;
using Microsoft.Playwright;

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

    public override async Task<object> Initialize(string configString, LogDelegate logFunction, INotificationService notificationService)
    {
        await base.Initialize(configString, logFunction, notificationService);

        try
        {
            var exitCode = Program.Main(["install", "chromium"]);
            if (exitCode != 0)
                await logFunction(LogLevel.Warning, $"Playwright browser install returned exit code {exitCode}");
        }
        catch (Exception ex)
        {
            await logFunction(LogLevel.Warning, $"Failed to install Playwright browsers: {ex.Message}");
        }

        return null;
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
