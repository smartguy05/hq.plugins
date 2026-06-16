using HQ.Plugins.Asana.Models;

namespace HQ.Plugins.Tests.Asana;

public class ServiceRequestTests
{
    [Fact]
    public void ServiceRequest_ShouldInitializeWithNullValues()
    {
        var request = new ServiceRequest();
        Assert.Null(request.Method);
        Assert.Null(request.TaskId);
        Assert.Null(request.Name);
        Assert.Null(request.Workspace);
        Assert.Null(request.ProjectId);
        Assert.Null(request.Query);
    }

    [Fact]
    public void ServiceRequest_NullableFields_DefaultToNull()
    {
        var request = new ServiceRequest();
        Assert.Null(request.Completed);
        Assert.Null(request.Archived);
        Assert.Null(request.SortAscending);
        Assert.Null(request.Limit);
        Assert.Null(request.Count);
        Assert.Null(request.IncludeSubtasks);
        Assert.Null(request.IncludeComments);
    }

    [Fact]
    public void ServiceRequest_ShouldSetTaskProperties()
    {
        var request = new ServiceRequest
        {
            Method = "create_task",
            TaskId = "12345",
            Name = "My Task",
            Notes = "Some notes",
            Assignee = "me",
            DueOn = "2026-06-01",
            StartOn = "2026-05-01",
            Completed = false,
            Parent = "67890",
            Followers = "user1,user2"
        };

        Assert.Equal("create_task", request.Method);
        Assert.Equal("12345", request.TaskId);
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
    public void ServiceRequest_ShouldSetProjectProperties()
    {
        var request = new ServiceRequest
        {
            ProjectId = "proj123",
            SectionId = "sec456",
            Workspace = "ws789",
            Team = "team001",
            Archived = true
        };

        Assert.Equal("proj123", request.ProjectId);
        Assert.Equal("sec456", request.SectionId);
        Assert.Equal("ws789", request.Workspace);
        Assert.Equal("team001", request.Team);
        Assert.True(request.Archived);
    }

    [Fact]
    public void ServiceRequest_ShouldSetSearchProperties()
    {
        var request = new ServiceRequest
        {
            Text = "search text",
            Query = "typeahead query",
            ResourceType = "task",
            AssigneeAny = "user1,user2",
            ProjectsAny = "proj1,proj2",
            DueOnBefore = "2026-12-31",
            DueOnAfter = "2026-01-01",
            SortBy = "due_date",
            SortAscending = true
        };

        Assert.Equal("search text", request.Text);
        Assert.Equal("typeahead query", request.Query);
        Assert.Equal("task", request.ResourceType);
        Assert.Equal("user1,user2", request.AssigneeAny);
        Assert.Equal("proj1,proj2", request.ProjectsAny);
        Assert.Equal("2026-12-31", request.DueOnBefore);
        Assert.Equal("2026-01-01", request.DueOnAfter);
        Assert.Equal("due_date", request.SortBy);
        Assert.True(request.SortAscending);
    }

    [Fact]
    public void ServiceRequest_ShouldSetStoryProperties()
    {
        var request = new ServiceRequest
        {
            StoryText = "A comment",
            HtmlText = "<p>A comment</p>"
        };

        Assert.Equal("A comment", request.StoryText);
        Assert.Equal("<p>A comment</p>", request.HtmlText);
    }

    [Fact]
    public void ServiceRequest_Completed_ShouldAcceptNull()
    {
        var request = new ServiceRequest { Completed = null };
        Assert.Null(request.Completed);
    }
}
