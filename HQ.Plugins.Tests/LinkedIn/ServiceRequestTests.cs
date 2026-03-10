using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.Tests.LinkedIn;

public class ServiceRequestTests
{
    [Fact]
    public void ServiceRequest_ShouldInitializeWithNullValues()
    {
        var request = new ServiceRequest();
        Assert.Null(request.Method);
        Assert.Null(request.Content);
        Assert.Null(request.LinkedInProfileUrl);
        Assert.Null(request.Keyword);
        Assert.Null(request.PostUrn);
    }

    [Fact]
    public void ServiceRequest_MaxResults_DefaultsTo10()
    {
        var request = new ServiceRequest();
        Assert.Equal(10, request.MaxResults);
    }

    [Fact]
    public void ServiceRequest_ShouldSetPostProperties()
    {
        var request = new ServiceRequest
        {
            Method = "create_post",
            Content = "Check out my latest article!",
            MediaUrl = "https://example.com/article",
            Visibility = "PUBLIC"
        };

        Assert.Equal("create_post", request.Method);
        Assert.Equal("Check out my latest article!", request.Content);
        Assert.Equal("https://example.com/article", request.MediaUrl);
        Assert.Equal("PUBLIC", request.Visibility);
    }

    [Fact]
    public void ServiceRequest_ShouldSetSearchProperties()
    {
        var request = new ServiceRequest
        {
            Keyword = ".NET developer",
            CurrentCompany = "Microsoft",
            CurrentRole = "Senior Engineer",
            Location = "Seattle",
            Industry = "Technology",
            MaxResults = 20
        };

        Assert.Equal(".NET developer", request.Keyword);
        Assert.Equal("Microsoft", request.CurrentCompany);
        Assert.Equal("Senior Engineer", request.CurrentRole);
        Assert.Equal("Seattle", request.Location);
        Assert.Equal("Technology", request.Industry);
        Assert.Equal(20, request.MaxResults);
    }

    [Fact]
    public void ServiceRequest_ShouldSetProfileLookupProperties()
    {
        var request = new ServiceRequest
        {
            LinkedInProfileUrl = "https://www.linkedin.com/in/someone",
            CompanyLinkedInUrl = "https://www.linkedin.com/company/example"
        };

        Assert.Equal("https://www.linkedin.com/in/someone", request.LinkedInProfileUrl);
        Assert.Equal("https://www.linkedin.com/company/example", request.CompanyLinkedInUrl);
    }
}
