using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Tasks.Models;
using HQ.Plugins.Tasks.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace HQ.Plugins.Tasks;

public class TasksCommand :
    CommandBase<ServiceRequest, ServiceConfig>,
    IHasFrontend,
    IDatabasePlugin,
    IHasHttpRoutes
{
    public override string Name => "Tasks";
    public override string Description =>
        "Self-hosted task manager: projects, tasks, comments. Local replacement for Asana.";

    protected override INotificationService NotificationService { get; set; }

    private static string _connectionString;

    public override Task<object> Initialize(string config, LogDelegate logFunction, INotificationService notificationService)
    {
        // Capture the shared Postgres connection once so HTTP endpoints can
        // build scoped DbContexts without an agent context.
        var conn = System.Environment.GetEnvironmentVariable("HQ_POSTGRES_CONNECTION");
        if (!string.IsNullOrWhiteSpace(conn)) _connectionString = conn;
        return base.Initialize(config, logFunction, notificationService);
    }

    // IDatabasePlugin --------------------------------------------------------

    public Type DbContextType => typeof(TasksDbContext);
    public string SchemaName => TasksDbContext.Schema;

    public async Task MigrateAsync(string connectionString)
    {
        _connectionString = connectionString;
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var ctx = new TasksDbContext(options);
        await ctx.Database.MigrateAsync();
    }

    // IHasFrontend -----------------------------------------------------------

    public FrontendManifest GetFrontendManifest() => new(
        EntryPath: "ui/index.html",
        Pages: new[]
        {
            new FrontendPage("/", "Tasks", IconName: "check-square", SidebarGroup: "Plugins")
        });

    // IHasHttpRoutes ---------------------------------------------------------

    public void MapRoutes(IEndpointRouteBuilder routes)
    {
        Endpoints.TasksEndpoints.Map(routes);
    }

    // ICommand (agent tools) -------------------------------------------------

    public override List<ToolCall> GetToolDefinitions()
        => Tools.TasksTools.GetToolDefinitions();

    protected override async Task<object> DoWork(
        ServiceRequest request, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        try
        {
            var impl = new Tools.TasksToolImpl();
            return await impl.ProcessRequest(RawServiceRequest, config, NotificationService);
        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, $"Tasks error: {ex.Message}", ex);
            return new { Success = false, Message = ex.Message };
        }
    }

    internal static TasksDbContext BuildDbContext()
    {
        var conn = _connectionString
            ?? System.Environment.GetEnvironmentVariable("HQ_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException(
                "Tasks: HQ_POSTGRES_CONNECTION environment variable is not set.");
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new TasksDbContext(options);
    }
}
