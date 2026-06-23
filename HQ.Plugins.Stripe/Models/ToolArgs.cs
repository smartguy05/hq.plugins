using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Stripe.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Money-moving tools support the confirmation flow, so their args types implement
/// <see cref="IPluginServiceRequest"/> (the framework envelope fields are <c>[Injected]</c> so
/// they are kept out of the schema yet preserved across the confirmation replay round-trip).
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

public class CreateInvoiceArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("Stripe customer ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CustomerId { get; set; }

    [Description("Line item amount in the smallest currency unit (cents)")]
    public long? Amount { get; set; }

    [Description("3-letter ISO currency, e.g. 'usd'")]
    public string Currency { get; set; }

    [Description("Line item / invoice description")]
    public string Description { get; set; }
}

public class SendInvoiceArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("The invoice ID to send")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string InvoiceId { get; set; }
}

public class ListInvoicesArgs
{
    [Description("Filter to this customer")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CustomerId { get; set; }

    [Description("Max results (default 20)")]
    public int? Limit { get; set; }
}

public class CreatePaymentLinkArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Description("Existing Stripe price ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string PriceId { get; set; }

    [Description("Amount in cents (if creating a new price)")]
    public long? Amount { get; set; }

    [Description("3-letter ISO currency")]
    public string Currency { get; set; }

    [Description("Product name (if creating a new price)")]
    public string ProductName { get; set; }

    [Description("Quantity (default 1)")]
    public long? Quantity { get; set; }
}

public class GetCustomerArgs
{
    [Required, Description("The customer ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CustomerId { get; set; }
}

public class SearchCustomersArgs
{
    [Required, Description("Stripe search query")]
    public string Query { get; set; }

    [Description("Max results (default 20)")]
    public int? Limit { get; set; }
}

public class CreateCustomerArgs
{
    [Required]
    public string Email { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }
}

public class ListPaymentsArgs
{
    [Description("Filter to this customer")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CustomerId { get; set; }

    [Description("Max results (default 20)")]
    public int? Limit { get; set; }
}

public class CreateRefundArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Description("PaymentIntent to refund")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string PaymentIntentId { get; set; }

    [Description("Charge to refund (alternative to paymentIntentId)")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ChargeId { get; set; }

    [Description("Partial refund amount in cents (omit for full refund)")]
    public long? Amount { get; set; }
}
