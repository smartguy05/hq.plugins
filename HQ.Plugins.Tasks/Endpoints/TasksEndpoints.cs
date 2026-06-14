using System;
using System.Threading.Tasks;
using HQ.Plugins.Tasks.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HQ.Plugins.Tasks.Endpoints;

public static class TasksEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        // Projects
        routes.MapGet("/projects", async (HttpContext ctx) =>
        {
            var orgId = ResolveOrg(ctx);
            if (orgId == Guid.Empty) return Results.BadRequest("Missing org");
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var list = await svc.ListProjectsAsync(orgId);
            return Results.Ok(list);
        });

        routes.MapPost("/projects", async (HttpContext ctx, ProjectCreate body) =>
        {
            var orgId = ResolveOrg(ctx);
            if (orgId == Guid.Empty) return Results.BadRequest("Missing org");
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var p = await svc.CreateProjectAsync(orgId, body.Name, body.Description, body.Color);
            return Results.Ok(p);
        });

        routes.MapPatch("/projects/{id:guid}", async (HttpContext ctx, Guid id, ProjectUpdate body) =>
        {
            var orgId = ResolveOrg(ctx);
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var p = await svc.UpdateProjectAsync(orgId, id, body.Name, body.Description, body.Color, body.Archived);
            return p == null ? Results.NotFound() : Results.Ok(p);
        });

        routes.MapDelete("/projects/{id:guid}", async (HttpContext ctx, Guid id) =>
        {
            var orgId = ResolveOrg(ctx);
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var ok = await svc.DeleteProjectAsync(orgId, id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // Tasks
        routes.MapGet("/tasks", async (HttpContext ctx, Guid? projectId, string status, string assignee) =>
        {
            var orgId = ResolveOrg(ctx);
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var list = await svc.ListTasksAsync(orgId, projectId, status, assignee);
            return Results.Ok(list);
        });

        routes.MapPost("/tasks", async (HttpContext ctx, TaskCreate body) =>
        {
            var orgId = ResolveOrg(ctx);
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var t = await svc.CreateTaskAsync(orgId, body.ProjectId, body.Title, body.Description, body.Assignee, body.Due);
            return Results.Ok(t);
        });

        routes.MapPatch("/tasks/{id:guid}", async (HttpContext ctx, Guid id, TaskUpdate body) =>
        {
            var orgId = ResolveOrg(ctx);
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var t = await svc.UpdateTaskAsync(orgId, id, body.Title, body.Description, body.Status,
                body.Assignee, body.Due, body.SortOrder);
            return t == null ? Results.NotFound() : Results.Ok(t);
        });

        routes.MapDelete("/tasks/{id:guid}", async (HttpContext ctx, Guid id) =>
        {
            var orgId = ResolveOrg(ctx);
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var ok = await svc.DeleteTaskAsync(orgId, id);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        // Comments
        routes.MapGet("/tasks/{id:guid}/comments", async (HttpContext ctx, Guid id) =>
        {
            var orgId = ResolveOrg(ctx);
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var list = await svc.ListCommentsAsync(orgId, id);
            return Results.Ok(list);
        });

        routes.MapPost("/tasks/{id:guid}/comments", async (HttpContext ctx, Guid id, CommentCreate body) =>
        {
            var orgId = ResolveOrg(ctx);
            using var db = TasksCommand.BuildDbContext();
            var svc = new TasksService(db);
            var c = await svc.AddCommentAsync(orgId, id, body.Author ?? "user", body.Text);
            return c == null ? Results.NotFound() : Results.Ok(c);
        });
    }

    private static Guid ResolveOrg(HttpContext ctx)
    {
        if (ctx.Request.Headers.TryGetValue("X-Organization-Id", out var h) &&
            Guid.TryParse(h.ToString(), out var orgId))
            return orgId;
        return Guid.Empty;
    }

    public record ProjectCreate(string Name, string Description, string Color);
    public record ProjectUpdate(string Name, string Description, string Color, bool? Archived);
    public record TaskCreate(Guid ProjectId, string Title, string Description, string Assignee, DateTime? Due);
    public record TaskUpdate(string Title, string Description, string Status, string Assignee, DateTime? Due, int? SortOrder);
    public record CommentCreate(string Text, string Author);
}
