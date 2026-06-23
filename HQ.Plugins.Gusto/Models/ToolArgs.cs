using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HQ.Plugins.Gusto.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. The <see cref="StringOrNumberConverter"/> is preserved on id fields that Gusto may emit
/// as either a string or a number.
/// </summary>

public class GetCompanyArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CompanyId { get; set; }
}

public class ListEmployeesArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CompanyId { get; set; }
}

public class GetEmployeeArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string EmployeeId { get; set; }
}

public class ListPayrollsArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CompanyId { get; set; }
}

public class GetPayrollArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CompanyId { get; set; }

    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string PayrollId { get; set; }
}

public class ListTimeOffRequestsArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CompanyId { get; set; }
}

public class ListLocationsArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CompanyId { get; set; }
}

public class ListPaySchedulesArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string CompanyId { get; set; }
}
