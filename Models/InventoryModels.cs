namespace Nop.Plugin.Misc.Inventory.Models;

/// <summary>One product on the inventory list — every product currently on the internal warehouse.</summary>
public class InventoryRow
{
    public int ProductId { get; set; }

    /// <summary>The attribute-combination id this row represents (0 = the product itself, no combination).</summary>
    public int CombinationId { get; set; }

    /// <summary>Human-readable variant label (e.g. the combination SKU) shown next to the product name; empty for non-combination rows.</summary>
    public string VariantInfo { get; set; }

    public string Sku { get; set; }
    public string Ean { get; set; }
    public string ProductName { get; set; }
    public string ManufacturerName { get; set; }
    public int VendorId { get; set; }
    public string VendorName { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }

    /// <summary>Comma-joined category names.</summary>
    public string Categories { get; set; }

    /// <summary>Category ids for in-memory filtering — a row matches the category filter if any id matches.</summary>
    public List<int> CategoryIds { get; set; } = new();

    public string ProductEditUrl { get; set; }
}

/// <summary>User-supplied filters for the inventory list (prefixed "Inv" to keep query params unambiguous).</summary>
public class InventoryFilters
{
    /// <summary>Which warehouse to inventory. Null → the resolved default warehouse; 0 → the "no warehouse"
    /// (direct-stock) mode; a positive id → that warehouse.</summary>
    public int? InvWarehouseId { get; set; }

    public int? InvManufacturerId { get; set; }
    public int? InvCategoryId { get; set; }

    /// <summary>Filter by supplier (Product.VendorId).</summary>
    public int? InvVendorId { get; set; }

    /// <summary>Price band (Product.Price), inclusive.</summary>
    public decimal? InvPriceFrom { get; set; }
    public decimal? InvPriceTo { get; set; }

    /// <summary>Stock-quantity range on the selected warehouse, inclusive.</summary>
    public int? InvStockFrom { get; set; }
    public int? InvStockTo { get; set; }

    /// <summary>Only products with a positive reserved quantity (open orders).</summary>
    public bool InvOnlyReserved { get; set; }

    /// <summary>If set (&gt; 0), keep only this many randomly chosen products — cycle counting.</summary>
    public int? InvSampleSize { get; set; }

    /// <summary>Sort key for the list: category (default), name, sku, stock, stockDesc, price, priceDesc,
    /// manufacturer, vendor.</summary>
    public string InvSortBy { get; set; }
}

/// <summary>One row in an active inventory session — snapshot + current count + product info.</summary>
public class InventorySessionRow
{
    public int ItemId { get; set; }
    public int ProductId { get; set; }

    /// <summary>The attribute-combination id this row counts (0 = the product itself, no combination).</summary>
    public int CombinationId { get; set; }

    /// <summary>Human-readable variant label (combination SKU) shown next to the product name; empty for non-combination rows.</summary>
    public string VariantInfo { get; set; }

    public string Sku { get; set; }
    public string Ean { get; set; }
    public string ProductName { get; set; }
    public string ManufacturerName { get; set; }
    public int SnapshotStock { get; set; }
    public int SnapshotReserved { get; set; }
    public int? CountedStock { get; set; }
    public bool IsConfirmed { get; set; }
    public string ProductEditUrl { get; set; }

    /// <summary>Current catalog price of the product — used to value the inventory difference in the report.</summary>
    public decimal Price { get; set; }

    /// <summary>True when this product's EAN/GTIN is shared by more than one catalog product, so a scan
    /// can't be resolved to a single product. The row is highlighted and scanning it is blocked.</summary>
    public bool IsDuplicateEan { get; set; }
}

/// <summary>Top-level state for one active inventory session.</summary>
public class InventorySessionModel
{
    public int SessionId { get; set; }
    public DateTime StartedOnUtc { get; set; }

    /// <summary>The warehouse this inventory counts (0 = "no warehouse" mode) and its display name.</summary>
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; }

    /// <summary>Free-form picker notes for this inventory.</summary>
    public string Notes { get; set; }

    /// <summary>Who carried out the inventory (nullable). Printed on the report's "counted by" line when set.</summary>
    public string CountedBy { get; set; }

    public List<InventorySessionRow> Rows { get; set; } = new();
}

/// <summary>One completed (locked) inventory in the history list — summary counts + stock-apply state.</summary>
public class InventoryCompletedRow
{
    public int SessionId { get; set; }
    public DateTime StartedOnUtc { get; set; }
    public DateTime? CompletedOnUtc { get; set; }

    /// <summary>The warehouse this inventory counted (0 = "no warehouse" mode) and its display name.</summary>
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; }

    public int ItemCount { get; set; }

    /// <summary>Confirmed items whose counted quantity differs from the snapshot (incl. shelf-added ones).</summary>
    public int MismatchCount { get; set; }

    /// <summary>Items that were never counted.</summary>
    public int PendingCount { get; set; }

    /// <summary>Money value of the discrepancy: Σ |counted − snapshot| × current price (mismatched items).</summary>
    public decimal DiscrepancyValue { get; set; }

    /// <summary>True once the one-shot stock correction has been applied.</summary>
    public bool StockApplied { get; set; }
    public DateTime? StockAppliedOnUtc { get; set; }
}

/// <summary>Page model for the inventory page: the read-only stock list + the active session (if any).</summary>
public class InventoryPageModel
{
    public List<InventoryRow> Inventory { get; set; } = new();
    public InventoryFilters InventoryFilters { get; set; } = new();

    /// <summary>Warehouse dropdown source — every real warehouse (id, name). The view adds the virtual
    /// "no warehouse" (id 0) option separately so its label stays localizable.</summary>
    public List<(int Id, string Name)> InventoryWarehouses { get; set; } = new();

    /// <summary>The currently selected warehouse id (the resolved default when none was supplied). 0 = "no warehouse".</summary>
    public int SelectedWarehouseId { get; set; }

    /// <summary>Manufacturer dropdown source — only manufacturers present in the unfiltered set.</summary>
    public List<(int Id, string Name)> InventoryManufacturers { get; set; } = new();

    /// <summary>Vendor (supplier) dropdown source — only vendors present in the unfiltered set.</summary>
    public List<(int Id, string Name)> InventoryVendors { get; set; } = new();

    /// <summary>Category dropdown source — only categories present in the unfiltered set (full breadcrumb path).</summary>
    public List<(int Id, string Name)> InventoryCategories { get; set; } = new();

    /// <summary>Active inventory sessions (up to <see cref="InventoryDefaults.MaxActiveSessions"/>), newest first.</summary>
    public List<InventorySessionModel> ActiveSessions { get; set; } = new();

    /// <summary>Completed (locked) inventories, newest first — the history tab.</summary>
    public List<InventoryCompletedRow> CompletedSessions { get; set; } = new();

    /// <summary>Search/filter model driving the inventory-list DataTables grid (the Selection tab).</summary>
    public InventorySearchModel InventorySearch { get; set; } = new();
}
