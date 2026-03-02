using HQ.Models.Interfaces;

namespace HQ.Plugins.HubSpot.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Contact fields
    public string ContactId { get; set; }
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Company { get; set; }
    public string JobTitle { get; set; }
    public string Phone { get; set; }
    public string LinkedInUrl { get; set; }
    public string Notes { get; set; }
    public string LifecycleStage { get; set; }

    // Deal fields
    public string DealId { get; set; }
    public string DealName { get; set; }
    public string DealStage { get; set; }
    public decimal? Amount { get; set; }
    public string CloseDate { get; set; }
    public string Pipeline { get; set; }

    // Company fields
    public string CompanyId { get; set; }
    public string CompanyName { get; set; }
    public string Domain { get; set; }
    public string Industry { get; set; }

    // Search/filter
    public string Query { get; set; }
    public int? MaxResults { get; set; } = 10;
    public string Properties { get; set; }

    // Note/association fields
    public string ObjectType { get; set; }
    public string ObjectId { get; set; }
}
