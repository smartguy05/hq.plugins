using HQ.Plugins.JobBoard.Models;

namespace HQ.Plugins.Tests.JobBoard;

public class ServiceRequestTests
{
    [Fact]
    public void ServiceRequest_ShouldInitializeWithDefaults()
    {
        var request = new ServiceRequest();
        Assert.Equal(10, request.MaxResults);
        Assert.Null(request.Method);
        Assert.Null(request.Query);
        Assert.Null(request.Source);
        Assert.Null(request.JobId);
    }

    [Fact]
    public void ServiceRequest_ShouldSetSearchProperties()
    {
        var request = new ServiceRequest
        {
            Method = "search_jobs",
            Query = ".NET contract developer",
            Location = "Remote",
            JobType = "contract",
            Source = "indeed",
            MaxResults = 20,
            MinSalary = "100000",
            Skills = "C#,.NET,Azure",
            PostedWithin = "7d"
        };

        Assert.Equal("search_jobs", request.Method);
        Assert.Equal(".NET contract developer", request.Query);
        Assert.Equal("Remote", request.Location);
        Assert.Equal("contract", request.JobType);
        Assert.Equal("indeed", request.Source);
        Assert.Equal(20, request.MaxResults);
        Assert.Equal("100000", request.MinSalary);
        Assert.Equal("C#,.NET,Azure", request.Skills);
        Assert.Equal("7d", request.PostedWithin);
    }

    [Fact]
    public void ServiceRequest_ShouldSetApplicationTrackingProperties()
    {
        var request = new ServiceRequest
        {
            JobId = "indeed-abc123",
            ApplicationId = "app-xyz",
            Status = "interviewing",
            Notes = "Phone screen went well"
        };

        Assert.Equal("indeed-abc123", request.JobId);
        Assert.Equal("app-xyz", request.ApplicationId);
        Assert.Equal("interviewing", request.Status);
        Assert.Equal("Phone screen went well", request.Notes);
    }

    [Fact]
    public void ServiceRequest_MaxResults_ShouldAcceptNull()
    {
        var request = new ServiceRequest { MaxResults = null };
        Assert.Null(request.MaxResults);
    }
}
