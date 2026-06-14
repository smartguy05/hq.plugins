using System;
using System.Threading.Tasks;
using HQ.Plugins.Tasks;
using HQ.Plugins.Tasks.Services;
using Microsoft.EntityFrameworkCore;

namespace HQ.Plugins.Tests.Tasks;

public class TasksServiceTests
{
    private static TasksDbContext NewCtx() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CreateAndListProjects_RoundTrip()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();

        var p = await svc.CreateProjectAsync(org, "Inbox", "default", "#888");
        Assert.NotEqual(Guid.Empty, p.Id);

        var list = await svc.ListProjectsAsync(org);
        Assert.Single(list);
        Assert.Equal("Inbox", list[0].Name);
    }

    [Fact]
    public async Task Projects_AreOrgScoped()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        await svc.CreateProjectAsync(orgA, "A", null, null);
        await svc.CreateProjectAsync(orgB, "B", null, null);

        Assert.Single(await svc.ListProjectsAsync(orgA));
        Assert.Single(await svc.ListProjectsAsync(orgB));
    }

    [Fact]
    public async Task CompleteTask_SetsStatusAndCompletedAt()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var project = await svc.CreateProjectAsync(org, "P", null, null);
        var task = await svc.CreateTaskAsync(org, project.Id, "Ship it", null, null, null);

        var updated = await svc.UpdateTaskAsync(org, task.Id, null, null, "done", null, null, null);
        Assert.Equal("done", updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task DeleteProject_CascadesTasksAndComments()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var project = await svc.CreateProjectAsync(org, "P", null, null);
        var task = await svc.CreateTaskAsync(org, project.Id, "T", null, null, null);
        await svc.AddCommentAsync(org, task.Id, "me", "hi");

        var ok = await svc.DeleteProjectAsync(org, project.Id);
        Assert.True(ok);
        Assert.Empty(await svc.ListProjectsAsync(org));
        Assert.Empty(await svc.ListTasksAsync(org, project.Id, null, null));
        Assert.Empty(await svc.ListCommentsAsync(org, task.Id));
    }

    [Fact]
    public async Task AddComment_RejectsCrossOrgTask()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var other = Guid.NewGuid();
        var project = await svc.CreateProjectAsync(org, "P", null, null);
        var task = await svc.CreateTaskAsync(org, project.Id, "T", null, null, null);

        var result = await svc.AddCommentAsync(other, task.Id, "user", "oops");
        Assert.Null(result);
    }

    [Fact]
    public async Task ListTasks_FiltersByStatus()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var project = await svc.CreateProjectAsync(org, "P", null, null);
        var t1 = await svc.CreateTaskAsync(org, project.Id, "A", null, null, null);
        var t2 = await svc.CreateTaskAsync(org, project.Id, "B", null, null, null);
        await svc.UpdateTaskAsync(org, t2.Id, null, null, "done", null, null, null);

        var todos = await svc.ListTasksAsync(org, project.Id, "todo", null);
        Assert.Single(todos);
        Assert.Equal(t1.Id, todos[0].Id);
    }
}
