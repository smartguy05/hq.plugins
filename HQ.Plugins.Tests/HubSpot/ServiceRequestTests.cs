using HQ.Plugins.HubSpot.Models;

namespace HQ.Plugins.Tests.HubSpot;

/// <summary>
/// Validates the per-tool argument records (see <c>ToolArgs.cs</c>) that replaced the old
/// property-bag <c>ServiceRequest</c> under the HQ.Models 2.7.0 typed-args migration. The
/// framework <see cref="ServiceRequest"/> now carries only orchestrator routing fields, so the
/// contact/deal/company/note fields these tests assert live on their dedicated args types.
/// </summary>
public class ServiceRequestTests
{
    [Fact]
    public void ServiceRequest_ShouldInitializeRoutingFieldsWithNullValues()
    {
        var request = new ServiceRequest();
        Assert.Null(request.Method);
        Assert.Null(request.ToolCallId);
        Assert.Null(request.RequestingService);
        Assert.Null(request.ConfirmationId);
    }

    [Fact]
    public void ArgsRecords_ShouldInitializeWithNullValues()
    {
        // Formerly asserted the property-bag defaults; the same fields now default to null on their
        // dedicated args records.
        Assert.Null(new UpdateContactArgs().ContactId);
        Assert.Null(new CreateContactArgs().Email);
        Assert.Null(new UpdateDealArgs().DealId);
        Assert.Null(new SearchContactsArgs().Query);
    }

    [Fact]
    public void SearchContactsArgs_MaxResults_DefaultsToNull()
    {
        // MaxResults is now nullable; the default (10) / cap (100) is applied in the service via
        // `request.MaxResults ?? 10`, not on the args record.
        var request = new SearchContactsArgs();
        Assert.Null(request.MaxResults);
    }

    [Fact]
    public void CreateContactArgs_ShouldSetContactProperties()
    {
        var request = new CreateContactArgs
        {
            Email = "test@example.com",
            FirstName = "John",
            LastName = "Doe",
            Company = "Acme Inc",
            JobTitle = "CTO",
            Phone = "+1234567890",
            LinkedInUrl = "https://linkedin.com/in/johndoe",
            LifecycleStage = "lead"
        };

        Assert.Equal("test@example.com", request.Email);
        Assert.Equal("John", request.FirstName);
        Assert.Equal("Doe", request.LastName);
        Assert.Equal("Acme Inc", request.Company);
        Assert.Equal("CTO", request.JobTitle);
        Assert.Equal("+1234567890", request.Phone);
        Assert.Equal("https://linkedin.com/in/johndoe", request.LinkedInUrl);
        Assert.Equal("lead", request.LifecycleStage);
    }

    [Fact]
    public void UpdateDealArgs_ShouldSetDealProperties()
    {
        var request = new UpdateDealArgs
        {
            DealId = "123",
            DealName = "Big Contract",
            DealStage = "contractsent",
            Amount = 50000.00m,
            CloseDate = "2026-06-01",
            Pipeline = "default"
        };

        Assert.Equal("123", request.DealId);
        Assert.Equal("Big Contract", request.DealName);
        Assert.Equal("contractsent", request.DealStage);
        Assert.Equal(50000.00m, request.Amount);
        Assert.Equal("2026-06-01", request.CloseDate);
        Assert.Equal("default", request.Pipeline);
    }

    [Fact]
    public void CreateCompanyArgs_ShouldSetCompanyProperties()
    {
        // Note: the old property-bag had a `CompanyId` field; there is no update_company tool in the
        // migrated surface, so CompanyId no longer exists on any HubSpot args record.
        var request = new CreateCompanyArgs
        {
            CompanyName = "Acme Corp",
            Domain = "acme.com",
            Industry = "Technology"
        };

        Assert.Equal("Acme Corp", request.CompanyName);
        Assert.Equal("acme.com", request.Domain);
        Assert.Equal("Technology", request.Industry);
    }

    [Fact]
    public void CreateDealArgs_Amount_ShouldAcceptNull()
    {
        var request = new CreateDealArgs { DealName = "d", Amount = null };
        Assert.Null(request.Amount);
    }

    [Fact]
    public void AddNoteArgs_ShouldSetNoteProperties()
    {
        var request = new AddNoteArgs
        {
            Notes = "Had a great meeting",
            ObjectType = "contacts",
            ObjectId = "789"
        };

        Assert.Equal("Had a great meeting", request.Notes);
        Assert.Equal("contacts", request.ObjectType);
        Assert.Equal("789", request.ObjectId);
    }
}
