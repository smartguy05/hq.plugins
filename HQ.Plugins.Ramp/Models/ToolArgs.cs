using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.Ramp.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

public class ListTransactionsArgs
{
    [Description("ISO date/time lower bound")]
    public string FromDate { get; set; }

    [Description("ISO date/time upper bound")]
    public string ToDate { get; set; }

    [Description("Max results (default 50)")]
    public int? PageSize { get; set; }
}

public class GetTransactionArgs
{
    [Required]
    public string TransactionId { get; set; }
}

public class ListCardsArgs
{
    [Description("Max results (default 50)")]
    public int? PageSize { get; set; }
}

public class GetCardArgs
{
    [Required]
    public string CardId { get; set; }
}

public class ListReimbursementsArgs
{
    [Description("Max results (default 50)")]
    public int? PageSize { get; set; }
}

public class ListUsersArgs
{
    [Description("Max results (default 50)")]
    public int? PageSize { get; set; }
}

public class ListDepartmentsArgs
{
    [Description("Max results (default 50)")]
    public int? PageSize { get; set; }
}

public class GetSpendLimitsArgs
{
    [Description("Max results (default 50)")]
    public int? PageSize { get; set; }
}
