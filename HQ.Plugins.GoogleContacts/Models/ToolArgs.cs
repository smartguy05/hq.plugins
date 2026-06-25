using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.GoogleContacts.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Descriptions are preserved verbatim from the previous hand-written JSON schemas.
/// </summary>

public class ListContactsArgs
{
    [Description("Max results (default 50, max 1000)")]
    public int? PageSize { get; set; }
}

public class SearchContactsArgs
{
    [Required]
    public string Query { get; set; }

    [Description("Max results (default 25, max 30)")]
    public int? PageSize { get; set; }
}

public class GetContactArgs
{
    [Required, Description("Contact resource name, e.g. people/c12345")]
    public string ResourceName { get; set; }
}

public class CreateContactArgs
{
    public string GivenName { get; set; }
    public string FamilyName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Organization { get; set; }
}

public class UpdateContactArgs
{
    [Required, Description("Contact resource name, e.g. people/c12345")]
    public string ResourceName { get; set; }
    public string GivenName { get; set; }
    public string FamilyName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Organization { get; set; }
}
