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
        var task = await svc.CreateTaskAsync(org, project.Id, null, null, "Ship it", null, null, null);

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
        var task = await svc.CreateTaskAsync(org, project.Id, null, null, "T", null, null, null);
        await svc.AddCommentAsync(org, task.Id, "me", "hi");

        var ok = await svc.DeleteProjectAsync(org, project.Id);
        Assert.True(ok);
        Assert.Empty(await svc.ListProjectsAsync(org));
        Assert.Empty(await svc.ListTasksAsync(org, project.Id, null, null, null));
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
        var task = await svc.CreateTaskAsync(org, project.Id, null, null, "T", null, null, null);

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
        var t1 = await svc.CreateTaskAsync(org, project.Id, null, null, "A", null, null, null);
        var t2 = await svc.CreateTaskAsync(org, project.Id, null, null, "B", null, null, null);
        await svc.UpdateTaskAsync(org, t2.Id, null, null, "done", null, null, null);

        var todos = await svc.ListTasksAsync(org, project.Id, null, "todo", null);
        Assert.Single(todos);
        Assert.Equal(t1.Id, todos[0].Id);
    }

    // --- Agent scoping ---------------------------------------------------------

    [Fact]
    public async Task CreateTask_WithoutProject_IsAgentScoped()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var agent = Guid.NewGuid();

        var task = await svc.CreateTaskAsync(org, null, agent, "Scout", "Jot this", null, null, null);

        Assert.Null(task.ProjectId);
        Assert.Equal(agent, task.AgentId);
        Assert.Equal("Scout", task.AgentName);
    }

    [Fact]
    public async Task CreateTask_WithNoScope_Throws()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateTaskAsync(Guid.NewGuid(), null, null, null, "X", null, null, null));
    }

    [Fact]
    public async Task ListTasks_ByAgent_ReturnsOnlyThatAgentsLooseTasks()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();
        var project = await svc.CreateProjectAsync(org, "Shared", null, null);

        await svc.CreateTaskAsync(org, null, agentA, "A", "mine", null, null, null);
        await svc.CreateTaskAsync(org, null, agentB, "B", "theirs", null, null, null);
        await svc.CreateTaskAsync(org, project.Id, null, null, "in-project", null, null, null);

        var forA = await svc.ListTasksAsync(org, null, agentA, null, null);
        Assert.Single(forA);
        Assert.Equal("mine", forA[0].Title);
    }

    [Fact]
    public async Task UpdateTask_AgentScoped_RejectsOtherAgent()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var task = await svc.CreateTaskAsync(org, null, owner, "Owner", "private", null, null, null);

        var blocked = await svc.UpdateTaskAsync(org, task.Id, "hacked", null, null, null, null, null, callerAgentId: intruder);
        Assert.Null(blocked);

        var allowed = await svc.UpdateTaskAsync(org, task.Id, "renamed", null, null, null, null, null, callerAgentId: owner);
        Assert.Equal("renamed", allowed.Title);
    }

    [Fact]
    public async Task UpdateTask_ProjectScoped_AllowsAnyAgent()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var project = await svc.CreateProjectAsync(org, "Shared", null, null);
        var task = await svc.CreateTaskAsync(org, project.Id, null, null, "T", null, null, null);

        var updated = await svc.UpdateTaskAsync(org, task.Id, "edited", null, null, null, null, null, callerAgentId: Guid.NewGuid());
        Assert.Equal("edited", updated.Title);
    }

    [Fact]
    public async Task ListAgents_ReturnsDistinctOwners()
    {
        using var ctx = NewCtx();
        var svc = new TasksService(ctx);
        var org = Guid.NewGuid();
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();
        await svc.CreateTaskAsync(org, null, agentA, "Alpha", "one", null, null, null);
        await svc.CreateTaskAsync(org, null, agentA, "Alpha", "two", null, null, null);
        await svc.CreateTaskAsync(org, null, agentB, "Beta", "three", null, null, null);

        var agents = await svc.ListAgentsAsync(org);
        Assert.Equal(2, agents.Count);
        Assert.Contains(agents, a => a.AgentId == agentA && a.AgentName == "Alpha");
        Assert.Contains(agents, a => a.AgentId == agentB && a.AgentName == "Beta");
    }
}
