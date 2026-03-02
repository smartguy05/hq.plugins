using HQ.Plugins.ReportGenerator.Models;

namespace HQ.Plugins.Tests.ReportGenerator;

public class ServiceRequestTests
{
    [Fact]
    public void ServiceRequest_ShouldInitializeWithDefaults()
    {
        var request = new ServiceRequest();
        Assert.Equal("html", request.Format);
        Assert.Null(request.Title);
        Assert.Null(request.Content);
        Assert.Null(request.FileName);
        Assert.Null(request.ReportId);
    }

    [Fact]
    public void ServiceRequest_ShouldSetAllProperties()
    {
        var request = new ServiceRequest
        {
            Method = "generate_report",
            Title = "Weekly Pipeline Report",
            Content = "## Summary\n\nAll deals on track.",
            Format = "markdown",
            FileName = "weekly-report",
            ReportId = "abc123"
        };

        Assert.Equal("generate_report", request.Method);
        Assert.Equal("Weekly Pipeline Report", request.Title);
        Assert.Equal("## Summary\n\nAll deals on track.", request.Content);
        Assert.Equal("markdown", request.Format);
        Assert.Equal("weekly-report", request.FileName);
        Assert.Equal("abc123", request.ReportId);
    }

    [Fact]
    public void ServiceRequest_Content_ShouldHandleLongStrings()
    {
        var longContent = new string('x', 50000);
        var request = new ServiceRequest { Content = longContent };
        Assert.Equal(50000, request.Content.Length);
    }
}
