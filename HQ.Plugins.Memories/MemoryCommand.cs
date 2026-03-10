using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Memories.Models;

namespace HQ.Plugins.Memories;

public class MemoryCommand: CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Memories";
    public override string Description => "A plugin to save memories for Agent use";
    protected override INotificationService NotificationService { get; set; }
    private static ChromaService _chromaService;

    public override List<ToolCall> GetToolDefinitions()
    {
        return ServiceExtensions.GetServiceToolCalls<ChromaService>();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> enumerableToolCalls)
    {
        try
        {
            _chromaService ??= new ChromaService(config, Log);
            return await _chromaService.ProcessRequest(serviceRequest, config, NotificationService);
        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'");
            
            return new
            {
                Success = false, 
                Message = $"Error: {ex.Message}"
            };
        }
    }

    public override async Task<object> Initialize(string configString, LogDelegate logFunction, INotificationService notificationService)
    {
        var config = configString.ReadPluginConfig<ServiceConfig>();
        _chromaService ??= new ChromaService(config, logFunction);
        var collection = await _chromaService.GetOrCreateCollectionClientAsync(config.DefaultCollectionName);

        if (collection is not null)
        {
            await logFunction(LogLevel.Info, $"Collection {config.DefaultCollectionName} exists in ChromaDB");

            // todo: Add logic to periodically review messages and save relevant information to memories
        }
        else
        {
            await logFunction(LogLevel.Warning, $"Collection {config.DefaultCollectionName} does not exist in ChromaDB");
        }

        return Task.CompletedTask;
    }
}