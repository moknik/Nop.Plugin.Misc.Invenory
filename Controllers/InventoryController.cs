using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.Inventory.Services;
using Nop.Services.Logging;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.Inventory.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class InventoryController : BasePluginController
{
    private readonly IInventoryService _service;
    private readonly ILogger _logger;

    public InventoryController(IInventoryService service, ILogger logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Inventory page — the read-only stock list + the active session (if any).</summary>
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_VIEW)]
    public async Task<IActionResult> Index([FromQuery] Models.InventoryFilters inventoryFilters)
    {
        var model = await _service.BuildInventoryPageAsync(inventoryFilters);
        return View("~/Plugins/Misc.Inventory/Views/Inventory/Index.cshtml", model);
    }

    /// <summary>Print view — same filtered stock list without the admin chrome. Auto-prints on load.</summary>
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_VIEW)]
    public async Task<IActionResult> Print([FromQuery] Models.InventoryFilters inventoryFilters)
    {
        var model = await _service.BuildInventoryPageAsync(inventoryFilters);
        return View("~/Plugins/Misc.Inventory/Views/Inventory/Print.cshtml", model);
    }

    /// <summary>Paged inventory-list data for the DataTables grid on the Selection tab (AJAX).</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_VIEW)]
    public async Task<IActionResult> InventoryList(Models.InventorySearchModel searchModel)
    {
        // DataTables server-side ordering: resolve the ordered column's data (property) name + direction.
        var form = Request.Form;
        var colIndex = form["order[0][column]"].FirstOrDefault();
        if (!string.IsNullOrEmpty(colIndex))
        {
            searchModel.OrderColumnName = form[$"columns[{colIndex}][data]"].FirstOrDefault();
            searchModel.OrderDesc = string.Equals(form["order[0][dir]"].FirstOrDefault(), "desc", StringComparison.OrdinalIgnoreCase);
        }

        var model = await _service.SearchInventoryAsync(searchModel);
        return Json(model);
    }

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Exports one session's items to an .xlsx file.</summary>
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_VIEW)]
    public async Task<IActionResult> ExportSession(int sessionId)
    {
        var bytes = await _service.ExportSessionToXlsxAsync(sessionId);
        if (bytes == null)
            return RedirectToAction(nameof(Index));
        return File(bytes, XlsxContentType, $"inventory-session-{sessionId}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    /// <summary>Downloads the inventory protocol of one session as a PDF.</summary>
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_VIEW)]
    public async Task<IActionResult> ReportPdf(int sessionId)
    {
        var bytes = await _service.BuildSessionReportPdfAsync(sessionId);
        if (bytes == null)
            return RedirectToAction(nameof(Index));
        return File(bytes, "application/pdf", $"inventory-report-{sessionId}-{DateTime.Now:yyyyMMdd-HHmm}.pdf");
    }

    /// <summary>
    /// Starts a new inventory session, snapshotting the products shown by the current inventory-tab
    /// filter. Redirects back to the inventory page. Silently no-ops if the active-session cap is hit.
    /// </summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> Start([FromForm] Models.InventoryFilters inventoryFilters)
    {
        try
        {
            await _service.StartSessionAsync(inventoryFilters);
        }
        catch (InvalidOperationException)
        {
            // Active-session cap reached — silently fall through, the existing tabs are already shown.
        }
        return RedirectToAction(nameof(Index), new { openSession = 1 });
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> Cancel(int sessionId)
    {
        await _service.CancelSessionAsync(sessionId);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Saves the free-form notes for a session (AJAX).</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> SaveNotes(int sessionId, string notes)
    {
        try
        {
            await _service.SetSessionNotesAsync(sessionId, notes);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Inventory.SaveNotes failed (session {sessionId})", ex);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Saves who carried out the inventory (AJAX).</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> SaveCountedBy(int sessionId, string countedBy)
    {
        try
        {
            await _service.SetSessionCountedByAsync(sessionId, countedBy);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Inventory.SaveCountedBy failed (session {sessionId})", ex);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>"OK — sedí počet kusů" — confirms the row at its snapshot stock (AJAX).</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> Confirm(int itemId)
    {
        try
        {
            await _service.ConfirmItemAsync(itemId);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Inventory.Confirm failed (item {itemId})", ex);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Records a counted quantity for an item (AJAX). Returns the new value.</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> SetCount(int itemId, int count)
    {
        try
        {
            await _service.SetItemCountAsync(itemId, count);
            return Json(new { success = true, count });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Inventory.SetCount failed (item {itemId}, count {count})", ex);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Adds a product found on the shelf but missing from the snapshot (AJAX). It lands as a mismatch
    /// (snapshot 0). Returns the row data + whether it's new so the UI can append or patch in place.
    /// </summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> AddProduct(int sessionId, string query, int count)
    {
        try
        {
            var (row, isNew) = await _service.AddProductToSessionAsync(sessionId, query, count);
            return Json(new
            {
                success = true,
                isNew,
                itemId = row.ItemId,
                productId = row.ProductId,
                combinationId = row.CombinationId,
                variantInfo = row.VariantInfo ?? string.Empty,
                sku = row.Sku ?? string.Empty,
                ean = row.Ean ?? string.Empty,
                productName = row.ProductName ?? string.Empty,
                manufacturerName = row.ManufacturerName ?? string.Empty,
                snapshotStock = row.SnapshotStock,
                snapshotReserved = row.SnapshotReserved,
                countedStock = row.CountedStock,
                productEditUrl = row.ProductEditUrl
            });
        }
        catch (DuplicateEanException ex)
        {
            // Expected operator input (a shared EAN) — surface it as a duplicate warning, don't log as error.
            return Json(new { success = false, duplicate = true, error = ex.Message });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Inventory.AddProduct failed (session {sessionId}, query {query}, count {count})", ex);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// Finish report — a print-friendly protocol of the mismatched + not-counted items, the notes and a
    /// signature line. Opens in a new tab; the session itself is left untouched.
    /// </summary>
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_VIEW)]
    public async Task<IActionResult> Report(int sessionId)
    {
        var model = await _service.GetSessionAsync(sessionId);
        if (model == null)
            return RedirectToAction(nameof(Index));

        return View("~/Plugins/Misc.Inventory/Views/Inventory/Report.cshtml", model);
    }

    /// <summary>Finishes and locks the inventory, moving it to the completed-inventories history.</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> Complete(int sessionId)
    {
        await _service.CompleteSessionAsync(sessionId);
        return RedirectToAction(nameof(Index), new { completed = 1 });
    }

    /// <summary>
    /// One-shot stock correction for a completed inventory — sets the W1 stock of the mismatched items to
    /// the counted value and writes the "change after inventory" stock-history movements. Can run once.
    /// </summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> ApplyStock(int sessionId)
    {
        try
        {
            await _service.ApplyStockChangesAsync(sessionId);
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Inventory.ApplyStock failed (session {sessionId})", ex);
        }
        return RedirectToAction(nameof(Index), new { completed = 1 });
    }

    /// <summary>Imports counted quantities into a session from an uploaded .xlsx (offline counting).</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> ImportCounts(int sessionId, IFormFile importFile)
    {
        try
        {
            if (importFile != null && importFile.Length > 0)
            {
                await using var stream = importFile.OpenReadStream();
                await _service.ImportSessionCountsAsync(sessionId, stream);
            }
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Inventory.ImportCounts failed (session {sessionId})", ex);
        }
        return RedirectToAction(nameof(Index), new { openSession = 1 });
    }

    /// <summary>Barcode-scanner entry — increments the matching session item by one piece (AJAX).</summary>
    [HttpPost]
    [CheckPermission(StandardPermission.Catalog.PRODUCTS_CREATE_EDIT_DELETE)]
    public async Task<IActionResult> Scan(int sessionId, string ean)
    {
        try
        {
            var (itemId, newCount) = await _service.IncrementByEanAsync(sessionId, ean);
            return Json(new { success = true, itemId, count = newCount });
        }
        catch (DuplicateEanException ex)
        {
            // Not an error to log — a duplicate EAN is expected operator input; the UI shows a warning.
            return Json(new { success = false, duplicate = true, error = ex.Message });
        }
        catch (Exception ex)
        {
            await _logger.ErrorAsync($"Inventory.Scan failed (session {sessionId}, ean {ean})", ex);
            return Json(new { success = false, error = ex.Message });
        }
    }
}
