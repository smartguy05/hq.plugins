using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;

namespace HQ.Plugins.QuickBooks.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. QBO IDs arrive as either strings or numbers, so id-bearing properties keep the
/// <see cref="StringOrNumberConverter"/>. Write tools that route through the confirmation flow
/// implement <see cref="IPluginServiceRequest"/> so the request survives the confirmation replay
/// round-trip; their framework envelope fields are <c>[Injected]</c> (bound, hidden from schema).
/// </summary>

// ───────────────────────────── Customers ─────────────────────────────

public class ListCustomersArgs
{
    [Description("Max results (default 50)")]
    public int? Limit { get; set; }
}

public class CreateCustomerArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("Customer display name (required, must be unique)")]
    public string DisplayName { get; set; }

    public string Email { get; set; }

    public string CompanyName { get; set; }
}

// ───────────────────────────── Invoices ─────────────────────────────

public class CreateInvoiceArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("Customer ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CustomerId { get; set; }

    [Required, Description("Line amount")]
    public decimal? Amount { get; set; }

    [Description("Sales item ID (default '1')")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    public string Description { get; set; }
}

public class SendInvoiceArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string InvoiceId { get; set; }

    [Description("Override recipient email (optional)")]
    public string SendTo { get; set; }
}

public class ListInvoicesArgs
{
    [Description("Max results (default 50)")]
    public int? Limit { get; set; }
}

// ───────────────────────────── Expenses / bills ─────────────────────────────

public class ListExpensesArgs
{
    [Description("Max results (default 50)")]
    public int? Limit { get; set; }
}

public class CreateExpenseArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("Account the money was paid FROM (bank/credit card)")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string PaymentAccountId { get; set; }

    [Required, Description("Expense category account ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ExpenseAccountId { get; set; }

    [Required]
    public decimal? Amount { get; set; }

    [Description("Cash | Check | CreditCard")]
    public string PaymentType { get; set; }

    [Description("Vendor/payee ID (optional)")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string VendorId { get; set; }

    public string Description { get; set; }
}

public class CreateBillArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string VendorId { get; set; }

    [Required, Description("Expense category account ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ExpenseAccountId { get; set; }

    [Required]
    public decimal? Amount { get; set; }

    public string Description { get; set; }
}

// ───────────────────────────── Reference data / reports ─────────────────────────────

public class ListAccountsArgs
{
    [Description("Max results (default 100)")]
    public int? Limit { get; set; }
}

public class ListVendorsArgs
{
    [Description("Max results (default 50)")]
    public int? Limit { get; set; }
}

public class RunReportArgs
{
    [Required, Description("ProfitAndLoss | BalanceSheet | AgedReceivables | etc.")]
    public string ReportName { get; set; }

    [Description("YYYY-MM-DD")]
    public string StartDate { get; set; }

    [Description("YYYY-MM-DD")]
    public string EndDate { get; set; }
}
