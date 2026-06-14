namespace HQ.Plugins.Zendesk;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on ZendeskService.</summary>
public static class ZendeskMethods
{
    public const string SearchTickets = "search_tickets";
    public const string GetTicket = "get_ticket";
    public const string CreateTicket = "create_ticket";
    public const string UpdateTicket = "update_ticket";
    public const string AddTicketComment = "add_ticket_comment";
    public const string ListTickets = "list_tickets";
    public const string GetUser = "get_user";
    public const string SearchUsers = "search_users";
    public const string ListMacros = "list_macros";
    public const string ApplyMacro = "apply_macro";
}
