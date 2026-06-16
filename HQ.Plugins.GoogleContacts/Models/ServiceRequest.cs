using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleContacts.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Listing / search
    public int? PageSize { get; set; }
    public string Query { get; set; }

    // Contact identity (resourceName like "people/c12345")
    public string ResourceName { get; set; }

    // Contact fields (create / update)
    public string GivenName { get; set; }
    public string FamilyName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Organization { get; set; }
}
