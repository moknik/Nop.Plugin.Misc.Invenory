using Nop.Plugin.Misc.Inventory.Models;

namespace Nop.Plugin.Misc.Inventory.Services;

public interface IInventoryService
{
    /// <summary>Builds the inventory page: the read-only internal-warehouse stock list + the active session (if any).</summary>
    Task<InventoryPageModel> BuildInventoryPageAsync(InventoryFilters inventoryFilters = null);

    /// <summary>Paged inventory-list grid data for the DataTables Selection tab (AJAX).</summary>
    Task<InventoryGridListModel> SearchInventoryAsync(InventorySearchModel searchModel);

    /// <summary>
    /// Snapshots the products shown by the inventory list under the supplied <paramref name="filters"/>
    /// (positive stock on the internal warehouse, matching manufacturer/category) into a new inventory
    /// session and marks it active. Throws if <see cref="InventoryDefaults.MaxActiveSessions"/> sessions
    /// are already active.
    /// </summary>
    Task<int> StartSessionAsync(InventoryFilters filters = null);

    /// <summary>Hard-deletes the session and all its items. No effect if the id doesn't exist.</summary>
    Task CancelSessionAsync(int sessionId);

    /// <summary>Saves the free-form notes for a session. No effect if the id doesn't exist.</summary>
    Task SetSessionNotesAsync(int sessionId, string notes);

    /// <summary>Saves who carried out the inventory. No effect if the id doesn't exist.</summary>
    Task SetSessionCountedByAsync(int sessionId, string countedBy);

    /// <summary>Marks a session item confirmed at its snapshot value (the OK button), keyed by item id.</summary>
    Task ConfirmItemAsync(int itemId);

    /// <summary>Records a counted quantity for a session item (by item id) and marks it confirmed. Does NOT touch real stock.</summary>
    Task SetItemCountAsync(int itemId, int count);

    /// <summary>
    /// Adds a product the picker physically found on the shelf but which wasn't part of the snapshot
    /// (not stocked in the system). Looks <paramref name="query"/> up against <c>Product.Sku</c> or
    /// <c>Product.Gtin</c> and inserts a new session item with <c>SnapshotStock = 0</c> and the given
    /// <paramref name="count"/> confirmed — so it shows as a mismatch ("Rozdíl"). If the product is
    /// already in the session, its count is set instead. Returns the row to render plus whether it was
    /// freshly added. Throws when the SKU/EAN isn't in the catalog or the session doesn't exist.
    /// </summary>
    Task<(InventorySessionRow Row, bool IsNew)> AddProductToSessionAsync(int sessionId, string query, int count);

    /// <summary>Loads a single session (active or not) with its rows for the finish report. Null if not found.</summary>
    Task<InventorySessionModel> GetSessionAsync(int sessionId);

    /// <summary>
    /// Finishes and locks the inventory: clears <see cref="InventorySession.IsActive"/> and stamps
    /// <see cref="InventorySession.CompletedOnUtc"/>, moving it to the completed-inventories history.
    /// Every still-uncounted item is treated as not found and recorded as counted 0 (confirmed), so it
    /// becomes a mismatch and the stock-correction button zeroes it out of the warehouse.
    /// No effect if the id doesn't exist or it is already completed.
    /// </summary>
    Task CompleteSessionAsync(int sessionId);

    /// <summary>
    /// One-shot stock correction for a completed inventory: for every mismatched item, sets the W1
    /// warehouse stock to the counted quantity and writes a stock-quantity-history movement ("Změna po
    /// inventuře"). Sets <see cref="InventorySession.StockApplied"/> so it can only run once. Returns the
    /// number of products adjusted. Throws if the session isn't completed or stock was already applied.
    /// </summary>
    Task<int> ApplyStockChangesAsync(int sessionId);

    /// <summary>Exports a session's items (snapshot/counted/difference/status) to an .xlsx workbook. Null if the session doesn't exist.</summary>
    Task<byte[]> ExportSessionToXlsxAsync(int sessionId);

    /// <summary>Builds the printable inventory protocol (mismatches, not-counted, discrepancy value, signature) as a PDF. Null if the session doesn't exist.</summary>
    Task<byte[]> BuildSessionReportPdfAsync(int sessionId);

    /// <summary>
    /// Imports counted quantities into an active session from an .xlsx in the ExportSession column layout
    /// (SKU col 3, EAN col 4, Counted col 6). Matches products by SKU then EAN; updates existing session
    /// items or adds found products not in the snapshot. Returns counts of applied rows and unmatched rows.
    /// </summary>
    Task<(int Updated, int NotFound)> ImportSessionCountsAsync(int sessionId, Stream stream);

    /// <summary>
    /// Barcode-scanner entry — looks the EAN up against <c>Product.Gtin</c>, finds the matching session
    /// item, increments its <c>CountedStock</c> by 1 and marks it confirmed. Returns the affected
    /// product id and the new total so the UI can patch the row in place. Throws when the EAN isn't
    /// part of this snapshot.
    /// </summary>
    Task<(int ItemId, int NewCount)> IncrementByEanAsync(int sessionId, string ean);
}
