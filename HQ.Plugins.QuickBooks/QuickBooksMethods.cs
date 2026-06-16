namespace HQ.Plugins.QuickBooks;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on QuickBooksService.</summary>
public static class QuickBooksMethods
{
    public const string ListCustomers = "list_customers";
    public const string CreateCustomer = "create_customer";
    public const string CreateInvoice = "create_invoice";
    public const string SendInvoice = "send_invoice";
    public const string ListInvoices = "list_invoices";
    public const string ListExpenses = "list_expenses";
    public const string CreateExpense = "create_expense";
    public const string ListAccounts = "list_accounts";
    public const string ListVendors = "list_vendors";
    public const string CreateBill = "create_bill";
    public const string RunReport = "run_report";
}
