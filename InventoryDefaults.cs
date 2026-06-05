namespace Nop.Plugin.Misc.Inventory;

/// <summary>Constants for the Inventory plugin.</summary>
public static class InventoryDefaults
{
    public const string SystemName = "Misc.Moknik.Inventory";
    public const string MenuSystemName = "Misc.Moknik.Inventory.Menu";
    public const string ResourceKeyPrefix = "Plugins.Misc.Moknik.Inventory";

    /// <summary>
    /// Sentinel warehouse id for the virtual "no warehouse" inventory mode — products that track stock
    /// directly via <c>Product.StockQuantity</c> (<c>ManageStock</c> + <c>UseMultipleWarehouses = false</c>).
    /// Matches nopCommerce's own convention that <c>WarehouseId = 0</c> means "no warehouse".
    /// </summary>
    public const int NoWarehouseId = 0;

    /// <summary>Maximum number of inventory sessions that can be active (counting) at the same time.</summary>
    public const int MaxActiveSessions = 5;
}
