using HQ.Plugins.Asana.Models;

namespace HQ.Plugins.Tests.Asana;

/// <summary>
/// Migrated from the pre-2.7.0 property-bag <c>ServiceRequest</c> to the per-tool args records in
/// <see cref="HQ.Plugins.Asana.Models"/> (ToolArgs.cs). Each former field now lives on the args type
/// of the tool that consumes it; these tests preserve the original intent/assertions against those
/// records. <see cref="ServiceRequest"/> itself is now only the orchestrator routing envelope.
/// </summary>
public class ToolArgsTests
{
    [Fact]
    public void ServiceRequest_IsRoutingEnvelope_WithNullDefaults()
    {
        var request = new ServiceRequest();
        Assert.Null(request.Method);
        Assert.Null(request.ToolCallId);
        Assert.Null(request.RequestingService);
        Assert.Null(request.ConfirmationId);
    }

    [Fact]
    public void ServiceRequest_ShouldSetRoutingFields()
    {
        var request = new ServiceRequest
        {
            Method = "create_task",
            ToolCallId = "call_1",
            RequestingService = "Asana",
            ConfirmationId = "conf_1"
        };

        Assert.Equal("create_task", request.Method);
        Assert.Equal("call_1", request.ToolCallId);
        Assert.Equal("Asana", request.RequestingService);
        Assert.Equal("conf_1", request.ConfirmationId);
    }

    [Fact]
    public void Args_ShouldInitializeWithNullValues()
    {
        Assert.Null(new GetTaskArgs().TaskId);
        Assert.Null(new CreateTaskArgs().Name);
        Assert.Null(new SearchTasksArgs().Workspace);
        Assert.Null(new GetProjectArgs().ProjectId);
        Assert.Null(new TypeaheadSearchArgs().Query);
    }

    [Fact]
    public void Args_NullableFields_DefaultToNull()
    {
        Assert.Null(new CreateTaskArgs().Completed);
        Assert.Null(new GetProjectsArgs().Archived);
        Assert.Null(new SearchTasksArgs().SortAscending);
        Assert.Null(new GetTasksArgs().Limit);
        Assert.Null(new TypeaheadSearchArgs().Count);
        Assert.Null(new GetTaskArgs().IncludeSubtasks);
        Assert.Null(new GetTaskArgs().IncludeComments);
    }

    [Fact]
    public void CreateTaskArgs_ShouldSetTaskProperties()
    {
        var request = new CreateTaskArgs
        {
            Name = "My Task",
            Notes = "Some notes",
            Assignee = "me",
            DueOn = "2026-06-01",
            StartOn = "2026-05-01",
            Completed = false,
            Parent = "67890",
            Followers = "user1,user2"
        };

        Assert.Equal("My Task", request.Name);
        Assert.Equal("Some notes", request.Notes);
        Assert.Equal("me", request.Assignee);
        Assert.Equal("2026-06-01", request.DueOn);
        Assert.Equal("2026-05-01", request.StartOn);
        Assert.False(request.Completed);
        Assert.Equal("67890", request.Parent);
        Assert.Equal("user1,user2", request.Followers);
    }

    [Fact]
    public void GetTaskArgs_ShouldSetTaskId()
    {
        var request = new GetTaskArgs { TaskId = "12345" };
        Assert.Equal("12345", request.TaskId);
    }

    [Fact]
    public void Args_ShouldSetProjectProperties()
    {
        var create = new CreateTaskArgs { ProjectId = "proj123", SectionId = "sec456" };
        Assert.Equal("proj123", create.ProjectId);
        Assert.Equal("sec456", create.SectionId);

        var projects = new GetProjectsArgs { Workspace = "ws789", Team = "team001", Archived = true };
        Assert.Equal("ws789", projects.Workspace);
        Assert.Equal("team001", projects.Team);
        Assert.True(projects.Archived);
    }

    [Fact]
    public void SearchTasksArgs_ShouldSetSearchProperties()
    {
        var request = new SearchTasksArgs
        {
            Text = "search text",
            AssigneeAny = "user1,user2",
            ProjectsAny = "proj1,proj2",
            DueOnBefore = "2026-12-31",
            DueOnAfter = "2026-01-01",
            SortBy = "due_date",
            SortAscending = true
        };

        Assert.Equal("search text", request.Text);
        Assert.Equal("user1,user2", request.AssigneeAny);
        Assert.Equal("proj1,proj2", request.ProjectsAny);
        Assert.Equal("2026-12-31", request.DueOnBefore);
        Assert.Equal("2026-01-01", request.DueOnAfter);
        Assert.Equal("due_date", request.SortBy);
        Assert.True(request.SortAscending);
    }

    [Fact]
    public void TypeaheadSearchArgs_ShouldSetSearchProperties()
    {
        var request = new TypeaheadSearchArgs { Query = "typeahead query", ResourceType = "task" };
        Assert.Equal("typeahead query", request.Query);
        Assert.Equal("task", request.ResourceType);
    }

    [Fact]
    public void CreateTaskStoryArgs_ShouldSetStoryProperties()
    {
        var request = new CreateTaskStoryArgs
        {
            StoryText = "A comment",
            HtmlText = "<p>A comment</p>"
        };

        Assert.Equal("A comment", request.StoryText);
        Assert.Equal("<p>A comment</p>", request.HtmlText);
    }

    [Fact]
    public void CreateTaskArgs_Completed_ShouldAcceptNull()
    {
        var request = new CreateTaskArgs { Completed = null };
        Assert.Null(request.Completed);
    }
}
