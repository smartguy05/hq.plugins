using HQ.Plugins.ReportGenerator.Models;

namespace HQ.Plugins.Tests.ReportGenerator;

public class ServiceRequestTests
{
    [Fact]
    public void Args_ShouldInitializeWithDefaults()
    {
        var generate = new GenerateReportArgs();
        // Format now defaults to null on the args record; the "html" fallback moved to the
        // command body (ReportGeneratorCommand.GenerateReport), so it is no longer a model default.
        Assert.Null(generate.Format);
        Assert.Null(generate.Title);
        Assert.Null(generate.Content);
        Assert.Null(generate.FileName);
        Assert.Null(new GetReportArgs().ReportId);
    }

    [Fact]
    public void Args_ShouldSetAllProperties()
    {
        var generate = new GenerateReportArgs
        {
            Title = "Weekly Pipeline Report",
            Content = "## Summary\n\nAll deals on track.",
            Format = "markdown",
            FileName = "weekly-report"
        };
        var get = new GetReportArgs { ReportId = "abc123" };
        var envelope = new ServiceRequest { Method = "generate_report" };

        Assert.Equal("generate_report", envelope.Method);
        Assert.Equal("Weekly Pipeline Report", generate.Title);
        Assert.Equal("## Summary\n\nAll deals on track.", generate.Content);
        Assert.Equal("markdown", generate.Format);
        Assert.Equal("weekly-report", generate.FileName);
        Assert.Equal("abc123", get.ReportId);
    }

    [Fact]
    public void Args_Content_ShouldHandleLongStrings()
    {
        var longContent = new string('x', 50000);
        var request = new GenerateReportArgs { Content = longContent };
        Assert.Equal(50000, request.Content.Length);
    }
}
