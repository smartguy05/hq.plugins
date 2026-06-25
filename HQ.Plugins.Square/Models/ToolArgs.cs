using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Square.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Booking writes notify customers, so they route through the confirmation flow; their args
/// types implement <see cref="IPluginServiceRequest"/> (the framework envelope fields are
/// <c>[Injected]</c> so they are kept out of the schema yet preserved across the confirmation
/// replay round-trip).
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

public class GetInventoryCountsArgs
{
    [Required, Description("Catalog item variation ID")]
    public string CatalogObjectId { get; set; }

    public string LocationId { get; set; }
}

public class ListCustomersArgs
{
    [Description("Max results (default 50)")]
    public int? Limit { get; set; }
}

public class SearchCustomersArgs
{
    [Required, Description("Email text to fuzzy-match")]
    public string Query { get; set; }
}

public class CreateCustomerArgs
{
    public string GivenName { get; set; }

    public string FamilyName { get; set; }

    public string Email { get; set; }

    public string Phone { get; set; }
}

public class ListPaymentsArgs
{
    public string LocationId { get; set; }

    [Description("Max results (default 50)")]
    public int? Limit { get; set; }
}

public class ListOrdersArgs
{
    public string LocationId { get; set; }
}

public class ListBookingsArgs
{
    public string LocationId { get; set; }

    [Description("Max results (default 50)")]
    public int? Limit { get; set; }
}

public class SearchAvailabilityArgs
{
    public string LocationId { get; set; }

    [Required, Description("Catalog service variation ID")]
    public string ServiceVariationId { get; set; }

    [Description("RFC3339 window start (default now)")]
    public string StartAt { get; set; }
}

public class CreateBookingArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    public string LocationId { get; set; }

    [Required]
    public string CustomerId { get; set; }

    [Required]
    public string ServiceVariationId { get; set; }

    [Required]
    public string TeamMemberId { get; set; }

    [Required, Description("RFC3339 appointment start time")]
    public string StartAt { get; set; }
}

public class CancelBookingArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required]
    public string BookingId { get; set; }
}
