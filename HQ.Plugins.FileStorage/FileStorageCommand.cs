using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.FileStorage.Models;

namespace HQ.Plugins.FileStorage;

public class FileStorageCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "HQ.Plugins.FileStorage";
    public override string Description => "Docker-based sandboxed file workspaces with Python and Node.js";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<FileStorageService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new FileStorageService(config, Logger);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new
            {
                Success = false,
                Message = $"Error: {e.Message}"
            };
        }
    }
}
