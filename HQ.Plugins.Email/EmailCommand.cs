using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Email.Data;
using HQ.Plugins.Email.Models;

namespace HQ.Plugins.Email;

public class EmailCommand: CommandBase<ServiceRequest,ServiceConfig>
{
    public override string Name => "Email";
    public override string Description => "Manage, search, send, delete email for the specified email accounts.";
    protected override INotificationService NotificationService { get; set; }

    private static LocalEmailStore _store;
    private static EmailVectorService _vectorService;
    private static EmailSyncEngine _syncEngine;

    public override List<ToolCall> GetToolDefinitions()
    {
        return new EmailService().GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> enumerableToolCalls)
    {
        var emailService = new EmailService(NotificationService, _store, _vectorService, _syncEngine);
        return await emailService.ProcessRequest(serviceRequest, config, NotificationService);
    }

    public override async Task<object> Initialize(string configString, LogDelegate logFunction, INotificationService notificationService)
    {
        NotificationService ??= notificationService;
        await logFunction(LogLevel.Info, "Initializing Email plugin");

        try
        {
            var config = configString.ReadPluginConfig<ServiceConfig>();

            // Auto-resolve SQLite path if not explicitly set
            var connString = config.SqliteConnectionString;
            if (string.IsNullOrWhiteSpace(connString))
            {
                var pluginDir = Path.GetDirectoryName(typeof(EmailCommand).Assembly.Location);
                var dbDir = Path.Combine(pluginDir!, "EmailData");
                Directory.CreateDirectory(dbDir);
                var dbName = !string.IsNullOrWhiteSpace(config.AgentId)
                    ? $"agent-{config.AgentId}-emails.db"
                    : "emails.db";
                connString = $"Data Source={Path.Combine(dbDir, dbName)}";
            }

            // Initialize SQLite store
            {
                _store ??= new LocalEmailStore(connString);
                await logFunction(LogLevel.Info, $"Email local store initialized: {connString}");
            }

            // Initialize ChromaDB vector service if configured
            if (!string.IsNullOrWhiteSpace(config.ChromaUrl) && !string.IsNullOrWhiteSpace(config.OpenAiApiKey))
            {
                _vectorService ??= new EmailVectorService(config, logFunction);
                await logFunction(LogLevel.Info, "Email vector search initialized");
            }

            // Initialize and start sync engine if store is available
            if (_store != null)
            {
                _syncEngine ??= new EmailSyncEngine(_store, _vectorService, config, logFunction);
                _syncEngine.StartBackground();
                await logFunction(LogLevel.Info, $"Email background sync started (interval: {config.SyncIntervalMinutes}m)");
            }
        }
        catch (Exception ex)
        {
            await logFunction(LogLevel.Error, $"Error initializing Email plugin: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
