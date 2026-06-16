namespace HQ.Plugins.Shopify;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on ShopifyService.</summary>
public static class ShopifyMethods
{
    public const string ListProducts = "list_products";
    public const string GetProduct = "get_product";
    public const string CreateProduct = "create_product";
    public const string UpdateInventory = "update_inventory";
    public const string ListOrders = "list_orders";
    public const string GetOrder = "get_order";
    public const string FulfillOrder = "fulfill_order";
    public const string ListCustomers = "list_customers";
    public const string SearchCustomers = "search_customers";
    public const string CreateDraftOrder = "create_draft_order";
}
