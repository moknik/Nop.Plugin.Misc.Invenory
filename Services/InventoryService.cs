using ClosedXML.Excel;
using iTextSharp.text;
using iTextSharp.text.pdf;
using LinqToDB;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Plugin.Misc.Inventory.Domain;
using Nop.Plugin.Misc.Inventory.Models;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Web.Framework.Models.Extensions;

namespace Nop.Plugin.Misc.Inventory.Services;

/// <summary>
/// Inventory data — a read-only internal-warehouse stock list plus inventory sessions (snapshot +
/// per-product counting). Each list is loaded with a handful of scoped SQL round-trips; the real
/// W1 stock is never modified (counts live only in the session).
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<ProductCategory> _productCategoryRepository;
    private readonly IRepository<ProductManufacturer> _productManufacturerRepository;
    private readonly IRepository<Manufacturer> _manufacturerRepository;
    private readonly IRepository<ProductWarehouseInventory> _productWarehouseRepository;
    private readonly IRepository<Category> _categoryRepository;
    private readonly IRepository<Warehouse> _warehouseRepository;
    private readonly IRepository<Vendor> _vendorRepository;
    private readonly IRepository<InventorySession> _inventorySessionRepository;
    private readonly IRepository<InventorySessionItem> _inventorySessionItemRepository;
    private readonly IRepository<ProductAttributeCombination> _combinationRepository;
    private readonly IProductAttributeService _productAttributeService;
    private readonly IProductService _productService;
    private readonly ILocalizationService _localizationService;
    private readonly IWorkContext _workContext;

    public InventoryService(
        IRepository<Product> productRepository,
        IRepository<ProductCategory> productCategoryRepository,
        IRepository<ProductManufacturer> productManufacturerRepository,
        IRepository<Manufacturer> manufacturerRepository,
        IRepository<ProductWarehouseInventory> productWarehouseRepository,
        IRepository<Category> categoryRepository,
        IRepository<Warehouse> warehouseRepository,
        IRepository<Vendor> vendorRepository,
        IRepository<InventorySession> inventorySessionRepository,
        IRepository<InventorySessionItem> inventorySessionItemRepository,
        IRepository<ProductAttributeCombination> combinationRepository,
        IProductAttributeService productAttributeService,
        IProductService productService,
        ILocalizationService localizationService,
        IWorkContext workContext)
    {
        _productRepository = productRepository;
        _productCategoryRepository = productCategoryRepository;
        _productManufacturerRepository = productManufacturerRepository;
        _manufacturerRepository = manufacturerRepository;
        _productWarehouseRepository = productWarehouseRepository;
        _categoryRepository = categoryRepository;
        _warehouseRepository = warehouseRepository;
        _vendorRepository = vendorRepository;
        _inventorySessionRepository = inventorySessionRepository;
        _inventorySessionItemRepository = inventorySessionItemRepository;
        _combinationRepository = combinationRepository;
        _productAttributeService = productAttributeService;
        _productService = productService;
        _localizationService = localizationService;
        _workContext = workContext;
    }

    public async Task<InventoryPageModel> BuildInventoryPageAsync(InventoryFilters inventoryFilters = null)
    {
        var filters = inventoryFilters ?? new InventoryFilters();

        // Resolve the warehouse: explicit selection wins, otherwise the default (the one used by the most
        // products; ties broken by the smallest id). Normalise it back onto the filters so the snapshot
        // and the dropdown agree.
        var warehouseNames = await LoadWarehouseNamesAsync();
        var selectedWarehouseId = filters.InvWarehouseId ?? await ResolveDefaultWarehouseIdAsync();
        filters.InvWarehouseId = selectedWarehouseId;

        var inventory = await BuildInventoryAsync(filters, selectedWarehouseId);
        var activeSessions = await BuildActiveSessionsAsync(warehouseNames);
        var completedSessions = await BuildCompletedSessionsAsync(warehouseNames);

        // Search model for the inventory-list grid: dropdown sources from the in-scope set + warehouses.
        var anyLabel = await LabelAsync("Filter.Any", "(any)");
        var search = new InventorySearchModel { InvWarehouseId = selectedWarehouseId };
        search.SetGridPageSize();
        search.AvailableWarehouses = warehouseNames
            .Select(kv => new SelectListItem { Value = kv.Key.ToString(), Text = kv.Value, Selected = kv.Key == selectedWarehouseId })
            .OrderBy(x => x.Text).ToList();
        search.AvailableWarehouses.Add(new SelectListItem
        {
            Value = "0",
            Text = await LabelAsync("Warehouse.None", "No warehouse"),
            Selected = selectedWarehouseId == InventoryDefaults.NoWarehouseId
        });
        search.AvailableManufacturers = BuildOptions(inventory.Manufacturers, anyLabel);
        search.AvailableVendors = BuildOptions(inventory.Vendors, anyLabel);
        search.AvailableCategories = BuildOptions(inventory.Categories, anyLabel);

        return new InventoryPageModel
        {
            Inventory = inventory.Rows,
            InventoryFilters = inventory.Filters,
            InventoryManufacturers = inventory.Manufacturers,
            InventoryCategories = inventory.Categories,
            InventoryVendors = inventory.Vendors,
            InventoryWarehouses = warehouseNames
                .Select(kv => (Id: kv.Key, Name: kv.Value))
                .OrderBy(t => t.Name)
                .ToList(),
            SelectedWarehouseId = selectedWarehouseId,
            ActiveSessions = activeSessions,
            CompletedSessions = completedSessions,
            InventorySearch = search
        };
    }

    /// <summary>Builds a "(any)" + items option list for a grid filter dropdown.</summary>
    private static List<SelectListItem> BuildOptions(List<(int Id, string Name)> items, string anyLabel)
    {
        var list = new List<SelectListItem> { new() { Value = "0", Text = anyLabel } };
        list.AddRange(items.Select(i => new SelectListItem { Value = i.Id.ToString(), Text = i.Name }));
        return list;
    }

    /// <summary>Server-side sort for the inventory grid by a column (an <see cref="InventoryItemModel"/>
    /// property name) + direction. Unknown/empty column keeps the default order from <see cref="BuildInventoryAsync"/>.</summary>
    private static List<InventoryRow> ApplyGridSort(List<InventoryRow> rows, string column, bool desc)
    {
        Func<InventoryRow, object> key = column switch
        {
            nameof(InventoryItemModel.ProductId) => r => r.ProductId,
            nameof(InventoryItemModel.ManufacturerName) => r => r.ManufacturerName ?? string.Empty,
            nameof(InventoryItemModel.VendorName) => r => r.VendorName ?? string.Empty,
            nameof(InventoryItemModel.ProductName) => r => r.ProductName ?? string.Empty,
            nameof(InventoryItemModel.VariantInfo) => r => r.VariantInfo ?? string.Empty,
            nameof(InventoryItemModel.Sku) => r => r.Sku ?? string.Empty,
            nameof(InventoryItemModel.Ean) => r => r.Ean ?? string.Empty,
            nameof(InventoryItemModel.Price) => r => r.Price,
            nameof(InventoryItemModel.StockQuantity) => r => r.StockQuantity,
            nameof(InventoryItemModel.ReservedQuantity) => r => r.ReservedQuantity,
            nameof(InventoryItemModel.Categories) => r => r.Categories ?? string.Empty,
            _ => null
        };
        if (key == null)
            return rows;
        return (desc ? rows.OrderByDescending(key) : rows.OrderBy(key)).ToList();
    }

    public async Task<InventoryGridListModel> SearchInventoryAsync(InventorySearchModel searchModel)
    {
        searchModel ??= new InventorySearchModel();

        var filters = new InventoryFilters
        {
            InvWarehouseId = searchModel.InvWarehouseId,
            InvManufacturerId = searchModel.InvManufacturerId == 0 ? null : searchModel.InvManufacturerId,
            InvVendorId = searchModel.InvVendorId == 0 ? null : searchModel.InvVendorId,
            InvCategoryId = searchModel.InvCategoryId == 0 ? null : searchModel.InvCategoryId,
            InvPriceFrom = searchModel.InvPriceFrom,
            InvPriceTo = searchModel.InvPriceTo,
            InvStockFrom = searchModel.InvStockFrom,
            InvStockTo = searchModel.InvStockTo,
            InvOnlyReserved = searchModel.InvOnlyReserved,
            InvSampleSize = searchModel.InvSampleSize
        };

        var inventory = await BuildInventoryAsync(filters, searchModel.InvWarehouseId);
        var sorted = ApplyGridSort(inventory.Rows, searchModel.OrderColumnName, searchModel.OrderDesc);
        var paged = sorted.ToPagedList(searchModel);

        return await new InventoryGridListModel().PrepareToGridAsync(searchModel, paged, () =>
            paged.SelectAwait(r => new ValueTask<InventoryItemModel>(new InventoryItemModel
            {
                ProductId = r.ProductId,
                CombinationId = r.CombinationId,
                VariantInfo = r.VariantInfo ?? string.Empty,
                Sku = r.Sku ?? string.Empty,
                Ean = r.Ean ?? string.Empty,
                ProductName = r.ProductName ?? string.Empty,
                ManufacturerName = r.ManufacturerName ?? string.Empty,
                VendorName = r.VendorName ?? string.Empty,
                Price = r.Price.ToString("0.##"),
                StockQuantity = r.StockQuantity,
                ReservedQuantity = r.ReservedQuantity,
                Categories = r.Categories ?? string.Empty,
                ProductEditUrl = r.ProductEditUrl
            })));
    }

    /// <summary>Every warehouse as id → name. Used for the dropdown and to label sessions.</summary>
    private async Task<Dictionary<int, string>> LoadWarehouseNamesAsync()
    {
        return (await _warehouseRepository.Table
                .Select(w => new { w.Id, w.Name })
                .ToListAsync())
            .ToDictionary(w => w.Id, w => w.Name);
    }

    /// <summary>
    /// The default warehouse: the one assigned to the most products (most distinct products with a
    /// <c>ProductWarehouseInventory</c> row), ties broken by the smallest warehouse id. Falls back to the
    /// smallest warehouse id when no product is warehouse-tracked, or to <see cref="InventoryDefaults.NoWarehouseId"/>
    /// when there are no warehouses at all.
    /// </summary>
    private async Task<int> ResolveDefaultWarehouseIdAsync()
    {
        var assignments = await _productWarehouseRepository.Table
            .Select(w => new { w.WarehouseId, w.ProductId })
            .Distinct()
            .ToListAsync();

        var byWarehouse = assignments
            .GroupBy(a => a.WarehouseId)
            .Select(g => (WarehouseId: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.WarehouseId)
            .ToList();

        if (byWarehouse.Count > 0)
            return byWarehouse[0].WarehouseId;

        var smallestWarehouseId = (await _warehouseRepository.Table
                .OrderBy(w => w.Id)
                .Select(w => (int?)w.Id)
                .Take(1)
                .ToListAsync())
            .FirstOrDefault();

        return smallestWarehouseId ?? InventoryDefaults.NoWarehouseId;
    }

    /// <summary>Completed (locked) inventories, newest first, with summary counts for the history tab.</summary>
    private async Task<List<InventoryCompletedRow>> BuildCompletedSessionsAsync(Dictionary<int, string> warehouseNames)
    {
        var sessions = await _inventorySessionRepository.Table
            .Where(s => !s.IsActive && s.CompletedOnUtc != null)
            .OrderByDescending(s => s.CompletedOnUtc)
            .ToListAsync();

        if (sessions.Count == 0)
            return new List<InventoryCompletedRow>();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var items = (await _inventorySessionItemRepository.Table
                .Where(i => sessionIds.Contains(i.SessionId))
                .Select(i => new { i.SessionId, i.ProductId, i.SnapshotStock, i.CountedStock, i.IsConfirmed })
                .ToListAsync())
            .GroupBy(i => i.SessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Current catalog prices for valuing the discrepancy (Σ |counted − snapshot| × price).
        var allProductIds = items.Values.SelectMany(l => l).Select(i => i.ProductId).Distinct().ToList();
        var prices = allProductIds.Count == 0
            ? new Dictionary<int, decimal>()
            : (await _productRepository.Table
                    .Where(p => allProductIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Price })
                    .ToListAsync())
                .ToDictionary(p => p.Id, p => p.Price);

        return sessions.Select(s =>
        {
            items.TryGetValue(s.Id, out var list);
            list ??= new();
            return new InventoryCompletedRow
            {
                SessionId = s.Id,
                StartedOnUtc = s.StartedOnUtc,
                CompletedOnUtc = s.CompletedOnUtc,
                WarehouseId = s.WarehouseId,
                WarehouseName = WarehouseDisplayName(s.WarehouseId, warehouseNames),
                ItemCount = list.Count,
                MismatchCount = list.Count(i => i.IsConfirmed && i.CountedStock != i.SnapshotStock),
                PendingCount = list.Count(i => !i.IsConfirmed),
                DiscrepancyValue = list
                    .Where(i => i.IsConfirmed && i.CountedStock != null && i.CountedStock != i.SnapshotStock)
                    .Sum(i => Math.Abs(i.CountedStock.Value - i.SnapshotStock)
                              * (prices.TryGetValue(i.ProductId, out var pr) ? pr : 0m)),
                StockApplied = s.StockApplied,
                StockAppliedOnUtc = s.StockAppliedOnUtc
            };
        }).ToList();
    }

    /// <summary>
    /// Every product currently sitting on the internal warehouse (W1) with positive stock. Dropdowns
    /// are computed from the unfiltered set, then the filter is applied in memory.
    /// </summary>
    private async Task<(
        List<InventoryRow> Rows,
        InventoryFilters Filters,
        List<(int Id, string Name)> Manufacturers,
        List<(int Id, string Name)> Categories,
        List<(int Id, string Name)> Vendors)> BuildInventoryAsync(InventoryFilters filters, int warehouseId)
    {
        // Normalize the dropdowns' "All" sentinel (0) to "no filter" so callers that bind filters straight
        // from the form (e.g. StartSessionAsync) match the grid path. Warehouse 0 is meaningful (no-warehouse
        // mode) and is intentionally left untouched.
        if (filters.InvManufacturerId == 0) filters.InvManufacturerId = null;
        if (filters.InvVendorId == 0) filters.InvVendorId = null;
        if (filters.InvCategoryId == 0) filters.InvCategoryId = null;

        // Step 1: the in-scope stock units. A unit is (product, combination): CombinationId 0 = the
        // product itself. For a real warehouse these are its PWI rows with positive stock; for the
        // virtual "no warehouse" mode (id 0) they are the direct-stock products (ManageStock without
        // multiple warehouses) PLUS attribute combinations (ManageStockByAttributes), whose stock lives
        // on the combination and is warehouse-agnostic.
        var stockRows = new List<(int ProductId, int CombinationId, int StockQuantity, int ReservedQuantity)>();
        var combinationInfo = new Dictionary<int, (string Sku, string Gtin, decimal? OverriddenPrice)>();
        if (warehouseId == InventoryDefaults.NoWarehouseId)
        {
            stockRows.AddRange((await _productRepository.Table
                    .Where(p => !p.Deleted
                                && p.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStock
                                && !p.UseMultipleWarehouses
                                && p.StockQuantity > 0)
                    .Select(p => new { p.Id, p.StockQuantity })
                    .ToListAsync())
                .Select(p => (p.Id, 0, p.StockQuantity, 0)));

            var combos = await (
                from c in _combinationRepository.Table
                join p in _productRepository.Table on c.ProductId equals p.Id
                where !p.Deleted
                      && p.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStockByAttributes
                      && c.StockQuantity > 0
                select new { c.Id, c.ProductId, c.StockQuantity, c.Sku, c.Gtin, c.OverriddenPrice }).ToListAsync();
            foreach (var c in combos)
            {
                stockRows.Add((c.ProductId, c.Id, c.StockQuantity, 0));
                combinationInfo[c.Id] = (c.Sku, c.Gtin, c.OverriddenPrice);
            }
        }
        else
        {
            stockRows.AddRange((await _productWarehouseRepository.Table
                    .Where(w => w.WarehouseId == warehouseId && w.StockQuantity > 0)
                    .Select(w => new { w.ProductId, w.StockQuantity, w.ReservedQuantity })
                    .ToListAsync())
                .Select(w => (w.ProductId, 0, w.StockQuantity, w.ReservedQuantity)));
        }

        if (stockRows.Count == 0)
            return (new List<InventoryRow>(), filters, new(), new(), new());

        var productIds = stockRows.Select(r => r.ProductId).Distinct().ToList();

        // Step 2: products (name/sku/gtin/price/vendor).
        var products = (await _productRepository.Table
                .Where(p => productIds.Contains(p.Id) && !p.Deleted)
                .Select(p => new { p.Id, p.Sku, p.Gtin, p.Name, p.Price, p.VendorId })
                .ToListAsync())
            .ToDictionary(p => p.Id);

        // Vendor (supplier) names for the in-scope products.
        var vendorIds = products.Values.Select(p => p.VendorId).Where(id => id != 0).Distinct().ToList();
        var vendorNames = vendorIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _vendorRepository.Table
                    .Where(v => vendorIds.Contains(v.Id) && !v.Deleted)
                    .Select(v => new { v.Id, v.Name })
                    .ToListAsync())
                .ToDictionary(v => v.Id, v => v.Name);

        // Step 3: per-product manufacturer (first by display order).
        var manufacturerMap = (await (
                from pmm in _productManufacturerRepository.Table
                join m in _manufacturerRepository.Table on pmm.ManufacturerId equals m.Id
                where productIds.Contains(pmm.ProductId) && !m.Deleted
                select new { pmm.ProductId, m.Id, m.Name, pmm.DisplayOrder }).ToListAsync())
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DisplayOrder).First());

        // Step 4: categories per product, excluding the noisy root.
        var categoryRows = (await (
                from pcm in _productCategoryRepository.Table
                join c in _categoryRepository.Table on pcm.CategoryId equals c.Id
                where productIds.Contains(pcm.ProductId)
                      && !c.Deleted
                select new { pcm.ProductId, CategoryId = c.Id, c.Name, pcm.DisplayOrder }).ToListAsync())
            .GroupBy(x => x.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.DisplayOrder).Select(x => (Id: x.CategoryId, x.Name)).ToList());

        // Step 5: build rows (one per unit; drop any whose product vanished between queries). For a
        // combination unit the SKU/EAN/price come from the combination (falling back to the product).
        var rows = stockRows
            .Where(s => products.ContainsKey(s.ProductId))
            .Select(s =>
            {
                var p = products[s.ProductId];
                manufacturerMap.TryGetValue(s.ProductId, out var mfr);
                categoryRows.TryGetValue(s.ProductId, out var cats);
                cats ??= new List<(int Id, string Name)>();
                var isCombination = s.CombinationId != 0;
                combinationInfo.TryGetValue(s.CombinationId, out var ci);
                var sku = isCombination && !string.IsNullOrEmpty(ci.Sku) ? ci.Sku : p.Sku;
                var ean = isCombination && !string.IsNullOrEmpty(ci.Gtin) ? ci.Gtin : p.Gtin;
                var price = isCombination && ci.OverriddenPrice.HasValue ? ci.OverriddenPrice.Value : p.Price;
                return new InventoryRow
                {
                    ProductId = p.Id,
                    CombinationId = s.CombinationId,
                    VariantInfo = isCombination ? (string.IsNullOrEmpty(ci.Sku) ? $"#{s.CombinationId}" : ci.Sku) : string.Empty,
                    Sku = sku,
                    Ean = ean,
                    ProductName = p.Name,
                    ManufacturerName = mfr?.Name ?? string.Empty,
                    VendorId = p.VendorId,
                    VendorName = p.VendorId != 0 && vendorNames.TryGetValue(p.VendorId, out var vn) ? vn : string.Empty,
                    Price = price,
                    StockQuantity = s.StockQuantity,
                    ReservedQuantity = s.ReservedQuantity,
                    Categories = string.Join(", ", cats.Select(c => c.Name)),
                    CategoryIds = cats.Select(c => c.Id).ToList(),
                    ProductEditUrl = $"/Admin/Product/Edit/{p.Id}"
                };
            })
            .ToList();

        // Step 6: dropdown sources from the unfiltered set, distinct + sorted.
        var manufacturerDropdown = manufacturerMap.Values
            .GroupBy(x => x.Id)
            .Select(g => (Id: g.Key, Name: g.First().Name))
            .OrderBy(t => t.Name)
            .ToList();

        var vendorDropdown = products.Values
            .Select(p => p.VendorId)
            .Where(id => id != 0)
            .Distinct()
            .Select(id => (Id: id, Name: vendorNames.TryGetValue(id, out var n) ? n : $"#{id}"))
            .OrderBy(t => t.Name)
            .ToList();

        // Show the full breadcrumb path in the dropdown (the per-row Categories string stays short).
        var categoryPaths = await LoadCategoryPathsAsync();
        var categoryDropdown = categoryRows.Values
            .SelectMany(list => list)
            .GroupBy(c => c.Id)
            .Select(g => (Id: g.Key, Name: categoryPaths.TryGetValue(g.Key, out var p) ? p : g.First().Name))
            .OrderBy(t => t.Name)
            .ToList();

        // Step 7: apply filters in memory.
        var manufacturerIdByProduct = manufacturerMap.ToDictionary(kv => kv.Key, kv => kv.Value.Id);
        var filtered = rows.Where(r =>
                (!filters.InvManufacturerId.HasValue
                    || (manufacturerIdByProduct.TryGetValue(r.ProductId, out var mid) && mid == filters.InvManufacturerId.Value))
                && (!filters.InvCategoryId.HasValue
                    || r.CategoryIds.Contains(filters.InvCategoryId.Value)))
            .Where(r => !filters.InvVendorId.HasValue || r.VendorId == filters.InvVendorId.Value)
            .Where(r => !filters.InvPriceFrom.HasValue || r.Price >= filters.InvPriceFrom.Value)
            .Where(r => !filters.InvPriceTo.HasValue || r.Price <= filters.InvPriceTo.Value)
            .Where(r => !filters.InvStockFrom.HasValue || r.StockQuantity >= filters.InvStockFrom.Value)
            .Where(r => !filters.InvStockTo.HasValue || r.StockQuantity <= filters.InvStockTo.Value)
            .Where(r => !filters.InvOnlyReserved || r.ReservedQuantity > 0)
            .ToList();

        // Optional random sample (cycle counting): keep only N randomly chosen rows.
        if (filters.InvSampleSize.HasValue && filters.InvSampleSize.Value > 0 && filters.InvSampleSize.Value < filtered.Count)
            filtered = filtered.OrderBy(_ => Guid.NewGuid()).Take(filters.InvSampleSize.Value).ToList();

        var sorted = SortRows(filtered, filters.InvSortBy);

        return (sorted, filters, manufacturerDropdown, categoryDropdown, vendorDropdown);
    }

    /// <summary>Sorts the inventory rows by the chosen key (category by default).</summary>
    private static List<InventoryRow> SortRows(List<InventoryRow> rows, string sortBy)
    {
        return (sortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "name" => rows.OrderBy(r => r.ProductName).ToList(),
            "sku" => rows.OrderBy(r => r.Sku ?? string.Empty).ToList(),
            "stock" => rows.OrderBy(r => r.StockQuantity).ThenBy(r => r.ProductName).ToList(),
            "stockdesc" => rows.OrderByDescending(r => r.StockQuantity).ThenBy(r => r.ProductName).ToList(),
            "price" => rows.OrderBy(r => r.Price).ThenBy(r => r.ProductName).ToList(),
            "pricedesc" => rows.OrderByDescending(r => r.Price).ThenBy(r => r.ProductName).ToList(),
            "manufacturer" => rows.OrderBy(r => r.ManufacturerName ?? string.Empty).ThenBy(r => r.ProductName).ToList(),
            "vendor" => rows.OrderBy(r => r.VendorName ?? string.Empty).ThenBy(r => r.ProductName).ToList(),
            _ => rows.OrderBy(r => r.Categories ?? string.Empty).ThenBy(r => r.ProductName).ToList(),
        };
    }

    /// <summary>
    /// Loads every active inventory session (newest first, up to the active-session cap) with its rows
    /// joined to current product info. Product/manufacturer lookups are batched across all sessions.
    /// </summary>
    private async Task<List<InventorySessionModel>> BuildActiveSessionsAsync(Dictionary<int, string> warehouseNames)
    {
        var sessions = await _inventorySessionRepository.Table
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.StartedOnUtc)
            .ToListAsync();

        return await BuildSessionModelsAsync(sessions, warehouseNames);
    }

    /// <summary>Builds the row-level model for the supplied sessions; lookups are batched across all of them.</summary>
    private async Task<List<InventorySessionModel>> BuildSessionModelsAsync(
        List<InventorySession> sessions, Dictionary<int, string> warehouseNames = null)
    {
        if (sessions.Count == 0)
            return new List<InventorySessionModel>();

        warehouseNames ??= await LoadWarehouseNamesAsync();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var allItems = (await _inventorySessionItemRepository.Table
                .Where(i => sessionIds.Contains(i.SessionId))
                .ToListAsync())
            .GroupBy(i => i.SessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var productIds = allItems.Values.SelectMany(list => list).Select(i => i.ProductId).Distinct().ToList();

        var products = productIds.Count == 0
            ? new Dictionary<int, (string Sku, string Gtin, string Name, decimal Price)>()
            : (await _productRepository.Table
                    .Where(p => productIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Sku, p.Gtin, p.Name, p.Price })
                    .ToListAsync())
                .ToDictionary(p => p.Id, p => (p.Sku, p.Gtin, p.Name, p.Price));

        // Combination details for combination items (their own SKU/GTIN/price overrides the product's).
        var combinationIds = allItems.Values.SelectMany(l => l)
            .Where(i => i.CombinationId != 0).Select(i => i.CombinationId).Distinct().ToList();
        var combinations = combinationIds.Count == 0
            ? new Dictionary<int, (string Sku, string Gtin, decimal? OverriddenPrice)>()
            : (await _combinationRepository.Table
                    .Where(c => combinationIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Sku, c.Gtin, c.OverriddenPrice })
                    .ToListAsync())
                .ToDictionary(c => c.Id, c => (c.Sku, c.Gtin, c.OverriddenPrice));

        var manufacturerMap = productIds.Count == 0
            ? new Dictionary<int, string>()
            : (await (
                    from pmm in _productManufacturerRepository.Table
                    join m in _manufacturerRepository.Table on pmm.ManufacturerId equals m.Id
                    where productIds.Contains(pmm.ProductId) && !m.Deleted
                    select new { pmm.ProductId, m.Name, pmm.DisplayOrder }).ToListAsync())
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.DisplayOrder).First().Name);

        // EANs that more than one (non-deleted) catalog product shares — a scan of such a code can't be
        // resolved to a single product, so those rows are flagged once here and scanning them is blocked.
        var duplicateEans = await GetDuplicateEansAsync(
            products.Values.Select(p => p.Gtin).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList());

        return sessions.Select(session =>
        {
            allItems.TryGetValue(session.Id, out var items);
            items ??= new List<InventorySessionItem>();

            var rows = items
                .Where(i => products.ContainsKey(i.ProductId))
                .Select(i =>
                {
                    var p = products[i.ProductId];
                    var isComb = i.CombinationId != 0;
                    combinations.TryGetValue(i.CombinationId, out var ci);
                    var sku = isComb && !string.IsNullOrEmpty(ci.Sku) ? ci.Sku : p.Sku;
                    var ean = isComb && !string.IsNullOrEmpty(ci.Gtin) ? ci.Gtin : p.Gtin;
                    var price = isComb && ci.OverriddenPrice.HasValue ? ci.OverriddenPrice.Value : p.Price;
                    return new InventorySessionRow
                    {
                        ItemId = i.Id,
                        ProductId = i.ProductId,
                        CombinationId = i.CombinationId,
                        VariantInfo = isComb ? (string.IsNullOrEmpty(ci.Sku) ? $"#{i.CombinationId}" : ci.Sku) : string.Empty,
                        Sku = sku,
                        Ean = ean,
                        ProductName = p.Name,
                        ManufacturerName = manufacturerMap.TryGetValue(i.ProductId, out var mn) ? mn : string.Empty,
                        SnapshotStock = i.SnapshotStock,
                        SnapshotReserved = i.SnapshotReserved,
                        CountedStock = i.CountedStock,
                        IsConfirmed = i.IsConfirmed,
                        Price = price,
                        ProductEditUrl = $"/Admin/Product/Edit/{i.ProductId}",
                        IsDuplicateEan = !isComb && !string.IsNullOrWhiteSpace(p.Gtin) && duplicateEans.Contains(p.Gtin)
                    };
                })
                // Unconfirmed first — what the picker still needs to count. Within each group, by name.
                .OrderBy(r => r.IsConfirmed)
                .ThenBy(r => r.ProductName)
                .ToList();

            return new InventorySessionModel
            {
                SessionId = session.Id,
                StartedOnUtc = session.StartedOnUtc,
                WarehouseId = session.WarehouseId,
                WarehouseName = WarehouseDisplayName(session.WarehouseId, warehouseNames),
                Notes = session.Notes,
                CountedBy = session.CountedBy,
                Rows = rows
            };
        }).ToList();
    }

    /// <summary>Display name for a session's warehouse id: the warehouse name, or null for the "no warehouse"
    /// mode (the view substitutes a localized label).</summary>
    private static string WarehouseDisplayName(int warehouseId, Dictionary<int, string> warehouseNames)
    {
        if (warehouseId == InventoryDefaults.NoWarehouseId)
            return null;
        return warehouseNames != null && warehouseNames.TryGetValue(warehouseId, out var name) ? name : $"#{warehouseId}";
    }

    /// <summary>
    /// Of the supplied EANs/GTINs, returns the set that is shared by more than one non-deleted catalog
    /// product — i.e. the codes a barcode scan can't resolve to a single product. Evaluated in one query.
    /// </summary>
    private async Task<HashSet<string>> GetDuplicateEansAsync(List<string> eans)
    {
        if (eans == null || eans.Count == 0)
            return new HashSet<string>();

        return (await _productRepository.Table
                .Where(p => !p.Deleted && p.Gtin != null && eans.Contains(p.Gtin))
                .GroupBy(p => p.Gtin)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync())
            .ToHashSet();
    }

    /// <summary>
    /// Maps every non-deleted category id to its full breadcrumb path "Parent » Child » Grandchild"
    /// (walking <c>ParentCategoryId</c>, guarding against cycles) so subcategories are unambiguous.
    /// </summary>
    private async Task<Dictionary<int, string>> LoadCategoryPathsAsync()
    {
        var cats = await _categoryRepository.Table
            .Where(c => !c.Deleted)
            .Select(c => new { c.Id, c.Name, c.ParentCategoryId })
            .ToListAsync();

        var byId = cats.ToDictionary(c => c.Id);

        string BuildPath(int id)
        {
            var parts = new List<string>();
            var seen = new HashSet<int>();
            var cur = id;
            while (cur != 0 && byId.TryGetValue(cur, out var c) && seen.Add(cur))
            {
                parts.Add(c.Name);
                cur = c.ParentCategoryId;
            }
            parts.Reverse();
            return string.Join(" » ", parts);
        }

        return cats.ToDictionary(c => c.Id, c => BuildPath(c.Id));
    }

    public async Task<int> StartSessionAsync(InventoryFilters filters = null)
    {
        if (await _inventorySessionRepository.Table.CountAsync(s => s.IsActive) >= InventoryDefaults.MaxActiveSessions)
            throw new InvalidOperationException(
                $"Maximum of {InventoryDefaults.MaxActiveSessions} active inventory sessions reached.");

        filters ??= new InventoryFilters();

        // The session is bound to one warehouse (explicit selection or the resolved default). Snapshot
        // exactly what the inventory list shows for that warehouse under the current filter, so the
        // session and the list agree.
        var warehouseId = filters.InvWarehouseId ?? await ResolveDefaultWarehouseIdAsync();
        filters.InvWarehouseId = warehouseId;
        var inventory = await BuildInventoryAsync(filters, warehouseId);

        var session = new InventorySession
        {
            StartedOnUtc = DateTime.UtcNow,
            IsActive = true,
            WarehouseId = warehouseId
        };
        await _inventorySessionRepository.InsertAsync(session);

        if (inventory.Rows.Count > 0)
        {
            var items = inventory.Rows
                .Select(r => new InventorySessionItem
                {
                    SessionId = session.Id,
                    ProductId = r.ProductId,
                    CombinationId = r.CombinationId,
                    SnapshotStock = r.StockQuantity,
                    SnapshotReserved = r.ReservedQuantity,
                    IsConfirmed = false
                })
                .ToList();
            await _inventorySessionItemRepository.InsertAsync(items);
        }

        return session.Id;
    }

    public async Task CancelSessionAsync(int sessionId)
    {
        var items = await _inventorySessionItemRepository.Table
            .Where(i => i.SessionId == sessionId)
            .ToListAsync();

        if (items.Count > 0)
            await _inventorySessionItemRepository.DeleteAsync(items);

        var session = await _inventorySessionRepository.GetByIdAsync(sessionId);
        if (session != null)
            await _inventorySessionRepository.DeleteAsync(session);
    }

    public async Task SetSessionNotesAsync(int sessionId, string notes)
    {
        var session = await _inventorySessionRepository.GetByIdAsync(sessionId);
        if (session == null)
            return;

        session.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await _inventorySessionRepository.UpdateAsync(session);
    }

    public async Task SetSessionCountedByAsync(int sessionId, string countedBy)
    {
        var session = await _inventorySessionRepository.GetByIdAsync(sessionId);
        if (session == null)
            return;

        session.CountedBy = string.IsNullOrWhiteSpace(countedBy) ? null : countedBy.Trim();
        await _inventorySessionRepository.UpdateAsync(session);
    }

    public async Task ConfirmItemAsync(int itemId)
    {
        var item = await _inventorySessionItemRepository.GetByIdAsync(itemId)
            ?? throw new InvalidOperationException($"Inventory item {itemId} not found.");

        item.CountedStock = item.SnapshotStock;
        item.CountedAtUtc = DateTime.UtcNow;
        item.IsConfirmed = true;
        await _inventorySessionItemRepository.UpdateAsync(item);
    }

    public async Task SetItemCountAsync(int itemId, int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Counted quantity cannot be negative.");

        var item = await _inventorySessionItemRepository.GetByIdAsync(itemId)
            ?? throw new InvalidOperationException($"Inventory item {itemId} not found.");

        item.CountedStock = count;
        item.CountedAtUtc = DateTime.UtcNow;
        item.IsConfirmed = true;
        await _inventorySessionItemRepository.UpdateAsync(item);
    }

    public async Task<(InventorySessionRow Row, bool IsNew)> AddProductToSessionAsync(int sessionId, string query, int count)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("SKU or EAN must not be empty.", nameof(query));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Counted quantity cannot be negative.");

        query = query.Trim();

        _ = await _inventorySessionRepository.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException($"Inventory session {sessionId} not found.");

        // Match on SKU or EAN against products AND attribute combinations; a barcode reader fills the same
        // box as a hand-typed SKU. Take(2) each so a shared code is flagged as ambiguous, not guessed.
        var productMatches = await _productRepository.Table
            .Where(p => !p.Deleted && (p.Sku == query || p.Gtin == query))
            .Select(p => p.Id)
            .Take(2)
            .ToListAsync();
        var combMatches = await _combinationRepository.Table
            .Where(c => c.Sku == query || c.Gtin == query)
            .Select(c => new { c.Id, c.ProductId })
            .Take(2)
            .ToListAsync();

        var total = productMatches.Count + combMatches.Count;
        if (total == 0)
            throw new InvalidOperationException($"Product with SKU/EAN '{query}' not found in the catalog.");
        if (total > 1)
            throw new DuplicateEanException("Duplicate EAN — cannot unambiguously match a single product.");

        int productId;
        int combinationId;
        if (combMatches.Count == 1)
        {
            combinationId = combMatches[0].Id;
            productId = combMatches[0].ProductId;
        }
        else
        {
            productId = productMatches[0];
            combinationId = 0;
        }

        var product = (await _productRepository.Table
                .Where(p => p.Id == productId)
                .Select(p => new { p.Id, p.Sku, p.Gtin, p.Name, p.Price })
                .Take(1)
                .ToListAsync())
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Product {productId} not found.");

        (string Sku, string Gtin, decimal? OverriddenPrice) ci = (null, null, null);
        if (combinationId != 0)
        {
            var cc = (await _combinationRepository.Table
                    .Where(c => c.Id == combinationId)
                    .Select(c => new { c.Sku, c.Gtin, c.OverriddenPrice })
                    .Take(1)
                    .ToListAsync())
                .FirstOrDefault();
            if (cc != null)
                ci = (cc.Sku, cc.Gtin, cc.OverriddenPrice);
        }

        var existing = (await _inventorySessionItemRepository.Table
                .Where(i => i.SessionId == sessionId && i.ProductId == productId && i.CombinationId == combinationId)
                .Take(1)
                .ToListAsync())
            .FirstOrDefault();

        var isNew = existing == null;
        var item = existing ?? new InventorySessionItem
        {
            SessionId = sessionId,
            ProductId = productId,
            CombinationId = combinationId,
            SnapshotStock = 0,
            SnapshotReserved = 0
        };

        item.CountedStock = count;
        item.CountedAtUtc = DateTime.UtcNow;
        item.IsConfirmed = true;

        if (isNew)
            await _inventorySessionItemRepository.InsertAsync(item);
        else
            await _inventorySessionItemRepository.UpdateAsync(item);

        // First manufacturer by display order (matches the inventory list).
        var manufacturerName = (await (
                from pmm in _productManufacturerRepository.Table
                join m in _manufacturerRepository.Table on pmm.ManufacturerId equals m.Id
                where pmm.ProductId == productId && !m.Deleted
                orderby pmm.DisplayOrder
                select m.Name).Take(1).ToListAsync())
            .FirstOrDefault() ?? string.Empty;

        var isComb = combinationId != 0;
        var sku = isComb && !string.IsNullOrEmpty(ci.Sku) ? ci.Sku : product.Sku;
        var ean = isComb && !string.IsNullOrEmpty(ci.Gtin) ? ci.Gtin : product.Gtin;
        var price = isComb && ci.OverriddenPrice.HasValue ? ci.OverriddenPrice.Value : product.Price;

        var row = new InventorySessionRow
        {
            ItemId = item.Id,
            ProductId = productId,
            CombinationId = combinationId,
            VariantInfo = isComb ? (string.IsNullOrEmpty(ci.Sku) ? $"#{combinationId}" : ci.Sku) : string.Empty,
            Sku = sku,
            Ean = ean,
            ProductName = product.Name,
            ManufacturerName = manufacturerName,
            SnapshotStock = item.SnapshotStock,
            SnapshotReserved = item.SnapshotReserved,
            CountedStock = item.CountedStock,
            IsConfirmed = item.IsConfirmed,
            Price = price,
            ProductEditUrl = $"/Admin/Product/Edit/{productId}"
        };

        return (row, isNew);
    }

    public async Task<InventorySessionModel> GetSessionAsync(int sessionId)
    {
        var session = await _inventorySessionRepository.GetByIdAsync(sessionId);
        if (session == null)
            return null;

        var models = await BuildSessionModelsAsync(new List<InventorySession> { session });
        return models.FirstOrDefault();
    }

    public async Task CompleteSessionAsync(int sessionId)
    {
        var session = await _inventorySessionRepository.GetByIdAsync(sessionId);
        if (session == null || session.CompletedOnUtc.HasValue)
            return;

        // Anything left uncounted when the inventory is finished is treated as physically not found:
        // its counted quantity becomes 0. Since snapshot items always have positive stock, that turns
        // every "not counted" row into a mismatch (snapshot > 0, counted 0), so the one-shot stock
        // correction (ApplyStockChangesAsync) zeroes it out of the warehouse.
        var uncounted = await _inventorySessionItemRepository.Table
            .Where(i => i.SessionId == sessionId && !i.IsConfirmed)
            .ToListAsync();
        if (uncounted.Count > 0)
        {
            var countedAt = DateTime.UtcNow;
            foreach (var item in uncounted)
            {
                item.CountedStock = 0;
                item.CountedAtUtc = countedAt;
                item.IsConfirmed = true;
            }
            await _inventorySessionItemRepository.UpdateAsync(uncounted);
        }

        session.IsActive = false;
        session.CompletedOnUtc = DateTime.UtcNow;
        await _inventorySessionRepository.UpdateAsync(session);
    }

    public async Task<int> ApplyStockChangesAsync(int sessionId)
    {
        var session = await _inventorySessionRepository.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException($"Inventory session {sessionId} not found.");

        if (!session.CompletedOnUtc.HasValue)
            throw new InvalidOperationException("Inventory must be completed (locked) before stock can be applied.");

        if (session.StockApplied)
            throw new InvalidOperationException("Stock changes for this inventory have already been applied.");

        var warehouseId = session.WarehouseId;

        // Only confirmed items whose count differs from the snapshot (incl. shelf-added products, snapshot 0).
        var mismatches = await _inventorySessionItemRepository.Table
            .Where(i => i.SessionId == sessionId
                        && i.IsConfirmed
                        && i.CountedStock != null
                        && i.CountedStock != i.SnapshotStock)
            .ToListAsync();

        // Stamp the stock-history movement with the inventory date (when it was finished/locked,
        // falling back to the start) so the movement is traceable to a specific inventory.
        var inventoryDate = (session.CompletedOnUtc ?? session.StartedOnUtc).ToString("yyyy-MM-dd");
        var messageTemplate = await _localizationService.GetResourceAsync($"{InventoryDefaults.ResourceKeyPrefix}.StockHistoryMessage");
        if (string.IsNullOrEmpty(messageTemplate))
            messageTemplate = "Change after inventory {0}";
        var message = string.Format(messageTemplate, inventoryDate);

        var adjusted = 0;
        foreach (var item in mismatches)
        {
            var product = await _productService.GetProductByIdAsync(item.ProductId);
            if (product == null)
                continue;

            var target = item.CountedStock.Value;

            // A combination row corrects the combination's own stock; otherwise direct-stock mode (no
            // warehouse) or a product not using multiple warehouses corrects Product.StockQuantity, and a
            // warehouse-tracked product corrects the selected warehouse's PWI row.
            bool changed;
            if (item.CombinationId != 0)
                changed = await ApplyCombinationStockAsync(product, item.CombinationId, target, message);
            else if (warehouseId == InventoryDefaults.NoWarehouseId || !product.UseMultipleWarehouses)
                changed = await ApplyDirectStockAsync(product, target, message);
            else
                changed = await ApplyWarehouseStockAsync(product, warehouseId, target, message);

            if (changed)
                adjusted++;
        }

        session.StockApplied = true;
        session.StockAppliedOnUtc = DateTime.UtcNow;
        await _inventorySessionRepository.UpdateAsync(session);

        return adjusted;
    }

    /// <summary>Corrects a product's direct stock (Product.StockQuantity) to <paramref name="target"/> and
    /// logs the movement. Returns false (no-op) when it already matches.</summary>
    private async Task<bool> ApplyDirectStockAsync(Product product, int target, string message)
    {
        var previous = product.StockQuantity;
        if (previous == target)
            return false;

        product.StockQuantity = target;
        await _productService.UpdateProductAsync(product);
        await _productService.AddStockQuantityHistoryEntryAsync(product, target - previous, product.StockQuantity, product.WarehouseId, message);
        return true;
    }

    /// <summary>Corrects an attribute combination's stock to <paramref name="target"/> (cache-safe update via
    /// the product-attribute service) and logs the movement against the combination. No-op when unchanged.</summary>
    private async Task<bool> ApplyCombinationStockAsync(Product product, int combinationId, int target, string message)
    {
        var combination = await _productAttributeService.GetProductAttributeCombinationByIdAsync(combinationId);
        if (combination == null)
            return false;

        var previous = combination.StockQuantity;
        if (previous == target)
            return false;

        combination.StockQuantity = target;
        await _productAttributeService.UpdateProductAttributeCombinationAsync(combination);
        await _productService.AddStockQuantityHistoryEntryAsync(product, target - previous, combination.StockQuantity, 0, message, combinationId);
        return true;
    }

    /// <summary>Corrects a product's stock on a specific warehouse (its PWI row, created if missing) to
    /// <paramref name="target"/> and logs the movement. Returns false (no-op) when nothing changed.</summary>
    private async Task<bool> ApplyWarehouseStockAsync(Product product, int warehouseId, int target, string message)
    {
        var pwi = (await _productService.GetAllProductWarehouseInventoryRecordsAsync(product.Id))
            .FirstOrDefault(x => x.WarehouseId == warehouseId);

        if (pwi != null)
        {
            var previous = pwi.StockQuantity;
            if (previous == target)
                return false;

            pwi.StockQuantity = target;
            await _productService.UpdateProductWarehouseInventoryAsync(pwi);
            await _productService.AddStockQuantityHistoryEntryAsync(product, target - previous, pwi.StockQuantity, warehouseId, message);
            return true;
        }

        // No record on this warehouse yet (typically a shelf-added product) — create one.
        if (target <= 0)
            return false;

        pwi = new ProductWarehouseInventory
        {
            WarehouseId = warehouseId,
            ProductId = product.Id,
            StockQuantity = target,
            ReservedQuantity = 0
        };
        await _productService.InsertProductWarehouseInventoryAsync(pwi);
        await _productService.AddStockQuantityHistoryEntryAsync(product, target, pwi.StockQuantity, warehouseId, message);
        return true;
    }

    public async Task<(int ItemId, int NewCount)> IncrementByEanAsync(int sessionId, string ean)
    {
        if (string.IsNullOrWhiteSpace(ean))
            throw new ArgumentException("EAN must not be empty.", nameof(ean));

        ean = ean.Trim();

        // Resolve EAN against product GTINs and combination GTINs. The same code mapping to more than one
        // product/combination can't be unambiguously matched, so a duplicate is blocked (not guessed).
        var productIds = await _productRepository.Table
            .Where(p => !p.Deleted && p.Gtin == ean)
            .Select(p => p.Id)
            .ToListAsync();
        var combinationIds = await _combinationRepository.Table
            .Where(c => c.Gtin == ean)
            .Select(c => c.Id)
            .ToListAsync();

        var total = productIds.Count + combinationIds.Count;
        if (total == 0)
            throw new InvalidOperationException($"EAN '{ean}' not found in the product catalog.");
        if (total > 1)
            throw new DuplicateEanException("Duplicate EAN — cannot unambiguously match a single product.");

        // Find the matching snapshot item — a combination row when a combination matched, otherwise the
        // product's own row (CombinationId 0).
        var item = combinationIds.Count == 1
            ? (await _inventorySessionItemRepository.Table
                    .Where(i => i.SessionId == sessionId && i.CombinationId == combinationIds[0])
                    .Take(1).ToListAsync()).FirstOrDefault()
            : (await _inventorySessionItemRepository.Table
                    .Where(i => i.SessionId == sessionId && i.ProductId == productIds[0] && i.CombinationId == 0)
                    .Take(1).ToListAsync()).FirstOrDefault();

        if (item == null)
            throw new InvalidOperationException($"EAN '{ean}' is not part of this inventory snapshot.");

        item.CountedStock = (item.CountedStock ?? 0) + 1;
        item.CountedAtUtc = DateTime.UtcNow;
        item.IsConfirmed = true;
        await _inventorySessionItemRepository.UpdateAsync(item);

        return (item.Id, item.CountedStock.Value);
    }

    #region Export

    /// <summary>Resolves a localized resource, falling back to the supplied English default.</summary>
    private async Task<string> LabelAsync(string keySuffix, string fallback)
    {
        var languageId = (await _workContext.GetWorkingLanguageAsync()).Id;
        var v = await _localizationService.GetResourceAsync($"{InventoryDefaults.ResourceKeyPrefix}.{keySuffix}", languageId, false, string.Empty, true);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    public async Task<byte[]> ExportSessionToXlsxAsync(int sessionId)
    {
        var model = await GetSessionAsync(sessionId);
        if (model == null)
            return null;

        var headers = new[]
        {
            await LabelAsync("Column.Manufacturer", "Manufacturer"),
            await LabelAsync("Column.Product", "Product"),
            await LabelAsync("Column.Sku", "SKU"),
            await LabelAsync("Column.Ean", "EAN"),
            await LabelAsync("Column.Price", "Price"),
            await LabelAsync("SnapshotStock", "Snapshot stock"),
            await LabelAsync("CountedStock", "Counted")
        };

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Inventory");
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in model.Rows.OrderBy(x => x.ManufacturerName).ThenBy(x => x.ProductName))
        {
            var counted = row.CountedStock ?? 0;
            ws.Cell(r, 1).Value = row.ManufacturerName ?? string.Empty;
            ws.Cell(r, 2).Value = row.ProductName ?? string.Empty;
            ws.Cell(r, 3).Value = row.Sku ?? string.Empty;
            ws.Cell(r, 4).Value = row.Ean ?? string.Empty;
            ws.Cell(r, 5).Value = row.Price;
            ws.Cell(r, 6).Value = row.SnapshotStock;
            ws.Cell(r, 7).Value = row.IsConfirmed ? counted : 0;
            r++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// A Unicode-capable base font for the PDF: an embedded system TrueType (Identity-H) so Czech
    /// diacritics render; falls back to the built-in Helvetica with CP1250, then WinAnsi. For a fully
    /// cross-platform build a TTF should be bundled with the plugin instead of relying on a system font.
    /// </summary>
    private static BaseFont CreatePdfBaseFont()
    {
        try
        {
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(fontsDir))
            {
                var arial = Path.Combine(fontsDir, "arial.ttf");
                if (File.Exists(arial))
                    return BaseFont.CreateFont(arial, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
            }
        }
        catch { /* fall through to a built-in font */ }

        try { return BaseFont.CreateFont(BaseFont.HELVETICA, "Cp1250", BaseFont.NOT_EMBEDDED); }
        catch { return BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED); }
    }

    public async Task<byte[]> BuildSessionReportPdfAsync(int sessionId)
    {
        var model = await GetSessionAsync(sessionId);
        if (model == null)
            return null;

        var diffRows = model.Rows
            .Where(r => r.IsConfirmed && (r.CountedStock ?? 0) != r.SnapshotStock)
            .OrderBy(r => r.ManufacturerName).ThenBy(r => r.ProductName).ToList();
        var pendingRows = model.Rows
            .Where(r => !r.IsConfirmed)
            .OrderBy(r => r.ManufacturerName).ThenBy(r => r.ProductName).ToList();
        var diffValue = diffRows.Sum(r => Math.Abs((r.CountedStock ?? 0) - r.SnapshotStock) * r.Price);

        // Localized captions.
        var title = await LabelAsync("Report.Title", "Inventory report");
        var warehouseLabel = await LabelAsync("Filter.Warehouse", "Warehouse");
        var warehouseNone = await LabelAsync("Warehouse.None", "No warehouse");
        var startedLabel = await LabelAsync("StartedAt", "Started");
        var generatedLabel = await LabelAsync("Report.GeneratedAt", "Generated");
        var diffHeading = await LabelAsync("Report.DiffHeading", "Mismatched items");
        var diffValueLabel = await LabelAsync("Report.DiffValue", "Difference value");
        var pendingHeading = await LabelAsync("Report.PendingHeading", "Not counted items");
        var noDiff = await LabelAsync("Report.NoDiff", "No mismatches.");
        var noPending = await LabelAsync("Report.NoPending", "All items counted.");
        var colManufacturer = await LabelAsync("Column.Manufacturer", "Manufacturer");
        var colProduct = await LabelAsync("Column.Product", "Product");
        var colSku = await LabelAsync("Column.Sku", "SKU");
        var colEan = await LabelAsync("Column.Ean", "EAN");
        var colSnapshot = await LabelAsync("SnapshotStock", "Snapshot stock");
        var colCounted = await LabelAsync("CountedStock", "Counted");
        var colDifference = await LabelAsync("Report.Difference", "Difference");
        var notesLabel = await LabelAsync("Notes", "Notes");
        var signedByLabel = await LabelAsync("Report.SignedBy", "Counted by");

        var baseFont = CreatePdfBaseFont();
        var titleFont = new Font(baseFont, 16, Font.BOLD);
        var h2Font = new Font(baseFont, 12, Font.BOLD);
        var normal = new Font(baseFont, 9);
        var bold = new Font(baseFont, 9, Font.BOLD);
        var headBg = new BaseColor(238, 238, 238);

        using var ms = new MemoryStream();
        var doc = new Document(PageSize.A4, 36, 36, 36, 36);
        PdfWriter.GetInstance(doc, ms);
        doc.Open();

        doc.Add(new Paragraph(title, titleFont));
        var warehouse = string.IsNullOrEmpty(model.WarehouseName) ? warehouseNone : model.WarehouseName;
        doc.Add(new Paragraph(
            $"{warehouseLabel}: {warehouse}  ·  {startedLabel}: {model.StartedOnUtc:yyyy-MM-dd HH:mm}  ·  {generatedLabel}: {DateTime.Now:yyyy-MM-dd HH:mm}",
            normal));
        doc.Add(new Paragraph(" "));

        doc.Add(new Paragraph($"{diffHeading} ({diffRows.Count})  —  {diffValueLabel}: {diffValue:0.00}", h2Font));
        if (diffRows.Count == 0)
        {
            doc.Add(new Paragraph(noDiff, normal));
        }
        else
        {
            var t = new PdfPTable(7) { WidthPercentage = 100, SpacingBefore = 4 };
            t.SetWidths(new float[] { 18, 28, 13, 13, 9, 9, 10 });
            void HeadCell(string s) => t.AddCell(new PdfPCell(new Phrase(s, bold)) { BackgroundColor = headBg, Padding = 3 });
            HeadCell(colManufacturer); HeadCell(colProduct); HeadCell(colSku); HeadCell(colEan);
            HeadCell(colSnapshot); HeadCell(colCounted); HeadCell(colDifference);
            foreach (var r in diffRows)
            {
                var counted = r.CountedStock ?? 0;
                var diff = counted - r.SnapshotStock;
                t.AddCell(new PdfPCell(new Phrase(r.ManufacturerName ?? string.Empty, normal)) { Padding = 3 });
                t.AddCell(new PdfPCell(new Phrase(r.ProductName ?? string.Empty, normal)) { Padding = 3 });
                t.AddCell(new PdfPCell(new Phrase(r.Sku ?? string.Empty, normal)) { Padding = 3 });
                t.AddCell(new PdfPCell(new Phrase(r.Ean ?? string.Empty, normal)) { Padding = 3 });
                t.AddCell(new PdfPCell(new Phrase(r.SnapshotStock.ToString(), normal)) { Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                t.AddCell(new PdfPCell(new Phrase(counted.ToString(), bold)) { Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
                t.AddCell(new PdfPCell(new Phrase((diff > 0 ? "+" : "") + diff, normal)) { Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
            }
            doc.Add(t);
        }

        doc.Add(new Paragraph(" "));
        doc.Add(new Paragraph($"{pendingHeading} ({pendingRows.Count})", h2Font));
        if (pendingRows.Count == 0)
        {
            doc.Add(new Paragraph(noPending, normal));
        }
        else
        {
            var t = new PdfPTable(5) { WidthPercentage = 100, SpacingBefore = 4 };
            t.SetWidths(new float[] { 22, 36, 18, 18, 12 });
            void HeadCell(string s) => t.AddCell(new PdfPCell(new Phrase(s, bold)) { BackgroundColor = headBg, Padding = 3 });
            HeadCell(colManufacturer); HeadCell(colProduct); HeadCell(colSku); HeadCell(colEan); HeadCell(colSnapshot);
            foreach (var r in pendingRows)
            {
                t.AddCell(new PdfPCell(new Phrase(r.ManufacturerName ?? string.Empty, normal)) { Padding = 3 });
                t.AddCell(new PdfPCell(new Phrase(r.ProductName ?? string.Empty, normal)) { Padding = 3 });
                t.AddCell(new PdfPCell(new Phrase(r.Sku ?? string.Empty, normal)) { Padding = 3 });
                t.AddCell(new PdfPCell(new Phrase(r.Ean ?? string.Empty, normal)) { Padding = 3 });
                t.AddCell(new PdfPCell(new Phrase(r.SnapshotStock.ToString(), normal)) { Padding = 3, HorizontalAlignment = Element.ALIGN_RIGHT });
            }
            doc.Add(t);
        }

        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            doc.Add(new Paragraph(" "));
            doc.Add(new Paragraph(notesLabel, h2Font));
            doc.Add(new Paragraph(model.Notes, normal));
        }

        doc.Add(new Paragraph(" "));
        doc.Add(new Paragraph(" "));
        doc.Add(new Paragraph($"{signedByLabel}: {model.CountedBy}", normal));

        doc.Close();
        return ms.ToArray();
    }

    public async Task<(int Updated, int NotFound)> ImportSessionCountsAsync(int sessionId, Stream stream)
    {
        var session = await _inventorySessionRepository.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException($"Inventory session {sessionId} not found.");
        if (session.CompletedOnUtc.HasValue)
            throw new InvalidOperationException("Cannot import counts into a completed inventory.");

        // Read the uploaded workbook in the column order produced by ExportSession: SKU = col 3,
        // EAN = col 4, Counted = col 7 (cols 5 = Price, 6 = Snapshot stock). The picker fills the Counted
        // column offline and re-uploads.
        var parsed = new List<(string Sku, string Ean, int Count)>();
        using (var wb = new XLWorkbook(stream))
        {
            var ws = wb.Worksheets.First();
            var last = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (var r = 2; r <= last; r++)
            {
                if (!ws.Cell(r, 7).TryGetValue<double>(out var d))
                    continue;
                var count = (int)Math.Round(d);
                if (count < 0)
                    continue;
                parsed.Add((ws.Cell(r, 3).GetString().Trim(), ws.Cell(r, 4).GetString().Trim(), count));
            }
        }

        if (parsed.Count == 0)
            return (0, 0);

        // Resolve products in two batched queries (by SKU, then by EAN for rows without a SKU).
        var skus = parsed.Where(x => x.Sku.Length > 0).Select(x => x.Sku).Distinct().ToList();
        var eans = parsed.Where(x => x.Sku.Length == 0 && x.Ean.Length > 0).Select(x => x.Ean).Distinct().ToList();
        var bySku = skus.Count == 0
            ? new Dictionary<string, int>()
            : (await _productRepository.Table.Where(p => !p.Deleted && skus.Contains(p.Sku))
                    .Select(p => new { p.Id, p.Sku }).ToListAsync())
                .GroupBy(p => p.Sku).ToDictionary(g => g.Key, g => g.First().Id);
        var byEan = eans.Count == 0
            ? new Dictionary<string, int>()
            : (await _productRepository.Table.Where(p => !p.Deleted && eans.Contains(p.Gtin))
                    .Select(p => new { p.Id, p.Gtin }).ToListAsync())
                .GroupBy(p => p.Gtin).ToDictionary(g => g.Key, g => g.First().Id);

        var items = (await _inventorySessionItemRepository.Table
                .Where(i => i.SessionId == sessionId).ToListAsync())
            .ToDictionary(i => i.ProductId, i => i);

        var now = DateTime.UtcNow;
        var toUpdate = new List<InventorySessionItem>();
        var toInsert = new List<InventorySessionItem>();
        var updated = 0;
        var notFound = 0;

        foreach (var (sku, ean, count) in parsed)
        {
            int productId = 0;
            if (sku.Length > 0)
                bySku.TryGetValue(sku, out productId);
            else if (ean.Length > 0)
                byEan.TryGetValue(ean, out productId);

            if (productId == 0)
            {
                notFound++;
                continue;
            }

            if (items.TryGetValue(productId, out var item))
            {
                item.CountedStock = count;
                item.CountedAtUtc = now;
                item.IsConfirmed = true;
                if (!toUpdate.Contains(item))
                    toUpdate.Add(item);
            }
            else
            {
                var fresh = new InventorySessionItem
                {
                    SessionId = sessionId,
                    ProductId = productId,
                    SnapshotStock = 0,
                    SnapshotReserved = 0,
                    CountedStock = count,
                    CountedAtUtc = now,
                    IsConfirmed = true
                };
                items[productId] = fresh; // avoid a duplicate insert if the file repeats the product
                toInsert.Add(fresh);
            }
            updated++;
        }

        if (toUpdate.Count > 0)
            await _inventorySessionItemRepository.UpdateAsync(toUpdate);
        if (toInsert.Count > 0)
            await _inventorySessionItemRepository.InsertAsync(toInsert);

        return (updated, notFound);
    }

    #endregion
}
