namespace HQ.Plugins.Stripe;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on StripeService.</summary>
public static class StripeMethods
{
    public const string CreateInvoice = "create_invoice";
    public const string SendInvoice = "send_invoice";
    public const string ListInvoices = "list_invoices";
    public const string CreatePaymentLink = "create_payment_link";
    public const string GetCustomer = "get_customer";
    public const string SearchCustomers = "search_customers";
    public const string CreateCustomer = "create_customer";
    public const string ListPayments = "list_payments";
    public const string CreateRefund = "create_refund";
    public const string GetBalance = "get_balance";
}
