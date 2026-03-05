using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.JobBoard.Models;

namespace HQ.Plugins.JobBoard;

public class JobBoardCommand : CommandBase<ServiceRequest, ServiceConfig>, ICommand
{
    public override string Name => "Job Board";
    public override string Description => "Search for contract/freelance jobs across Indeed, Upwork, LinkedIn, and Toptal, and track applications";
    protected override INotificationService NotificationService { get; set; }

    private IFileStorageProvider _fileStorage;

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<JobBoardService>();
    }

    void ICommand.SetFileStorageProvider(IFileStorageProvider provider)
    {
        _fileStorage = provider;
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var service = new JobBoardService(config, Logger, _fileStorage);
            return await service.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", e);
            return new { Success = false, Message = $"Error: {e.Message}" };
        }
    }
}
