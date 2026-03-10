using HQ.Plugins.HubSpot.Models;

namespace HQ.Plugins.Tests.HubSpot;

public class ServiceRequestTests
{
    [Fact]
    public void ServiceRequest_ShouldInitializeWithNullValues()
    {
        var request = new ServiceRequest();
        Assert.Null(request.Method);
        Assert.Null(request.ContactId);
        Assert.Null(request.Email);
        Assert.Null(request.DealId);
        Assert.Null(request.CompanyId);
        Assert.Null(request.Query);
    }

    [Fact]
    public void ServiceRequest_MaxResults_DefaultsTo10()
    {
        var request = new ServiceRequest();
        Assert.Equal(10, request.MaxResults);
    }

    [Fact]
    public void ServiceRequest_ShouldSetContactProperties()
    {
        var request = new ServiceRequest
        {
            Method = "create_contact",
            Email = "test@example.com",
            FirstName = "John",
            LastName = "Doe",
            Company = "Acme Inc",
            JobTitle = "CTO",
            Phone = "+1234567890",
            LinkedInUrl = "https://linkedin.com/in/johndoe",
            LifecycleStage = "lead"
        };

        Assert.Equal("create_contact", request.Method);
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
    public void ServiceRequest_ShouldSetDealProperties()
    {
        var request = new ServiceRequest
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
    public void ServiceRequest_ShouldSetCompanyProperties()
    {
        var request = new ServiceRequest
        {
            CompanyId = "456",
            CompanyName = "Acme Corp",
            Domain = "acme.com",
            Industry = "Technology"
        };

        Assert.Equal("456", request.CompanyId);
        Assert.Equal("Acme Corp", request.CompanyName);
        Assert.Equal("acme.com", request.Domain);
        Assert.Equal("Technology", request.Industry);
    }

    [Fact]
    public void ServiceRequest_Amount_ShouldAcceptNull()
    {
        var request = new ServiceRequest { Amount = null };
        Assert.Null(request.Amount);
    }

    [Fact]
    public void ServiceRequest_ShouldSetNoteProperties()
    {
        var request = new ServiceRequest
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
