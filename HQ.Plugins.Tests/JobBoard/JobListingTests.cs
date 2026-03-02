using HQ.Plugins.JobBoard.Models;

namespace HQ.Plugins.Tests.JobBoard;

public class JobListingTests
{
    [Fact]
    public void JobListing_ShouldInitializeWithNullValues()
    {
        var listing = new JobListing();
        Assert.Null(listing.Id);
        Assert.Null(listing.Title);
        Assert.Null(listing.Company);
        Assert.Null(listing.Location);
        Assert.Null(listing.Description);
        Assert.Null(listing.Salary);
        Assert.Null(listing.JobType);
        Assert.Null(listing.Url);
        Assert.Null(listing.Source);
        Assert.Null(listing.PostedDate);
        Assert.Null(listing.Skills);
    }

    [Fact]
    public void JobListing_ShouldSetAllProperties()
    {
        var listing = new JobListing
        {
            Id = "indeed-abc123",
            Title = "Senior .NET Developer",
            Company = "Acme Corp",
            Location = "Remote",
            Description = "Build amazing things",
            Salary = "$150,000 - $200,000",
            JobType = "contract",
            Url = "https://indeed.com/job/123",
            Source = "indeed",
            PostedDate = "2026-02-28",
            Skills = "C#,.NET,Azure"
        };

        Assert.Equal("indeed-abc123", listing.Id);
        Assert.Equal("Senior .NET Developer", listing.Title);
        Assert.Equal("Acme Corp", listing.Company);
        Assert.Equal("Remote", listing.Location);
        Assert.Equal("Build amazing things", listing.Description);
        Assert.Equal("$150,000 - $200,000", listing.Salary);
        Assert.Equal("contract", listing.JobType);
        Assert.Equal("https://indeed.com/job/123", listing.Url);
        Assert.Equal("indeed", listing.Source);
        Assert.Equal("2026-02-28", listing.PostedDate);
        Assert.Equal("C#,.NET,Azure", listing.Skills);
    }
}
