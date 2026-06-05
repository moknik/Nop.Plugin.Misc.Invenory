using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.Inventory.Models;

/// <summary>Search/filter model for the inventory list DataTables grid (the Selection tab).</summary>
public partial record InventorySearchModel : BaseSearchModel
{
    public InventorySearchModel()
    {
        AvailableWarehouses = new List<SelectListItem>();
        AvailableManufacturers = new List<SelectListItem>();
        AvailableVendors = new List<SelectListItem>();
        AvailableCategories = new List<SelectListItem>();
    }

    /// <summary>Which warehouse to inventory (0 = the virtual "no warehouse"/direct-stock + combinations mode).</summary>
    public int InvWarehouseId { get; set; }

    public int InvManufacturerId { get; set; }
    public int InvVendorId { get; set; }
    public int InvCategoryId { get; set; }

    public decimal? InvPriceFrom { get; set; }
    public decimal? InvPriceTo { get; set; }
    public int? InvStockFrom { get; set; }
    public int? InvStockTo { get; set; }

    public bool InvOnlyReserved { get; set; }
    public int? InvSampleSize { get; set; }

    /// <summary>Grid column to sort by (an <see cref="InventoryItemModel"/> property name); set from the DataTables order params in the controller.</summary>
    public string OrderColumnName { get; set; }

    /// <summary>True for descending grid sort.</summary>
    public bool OrderDesc { get; set; }

    public IList<SelectListItem> AvailableWarehouses { get; set; }
    public IList<SelectListItem> AvailableManufacturers { get; set; }
    public IList<SelectListItem> AvailableVendors { get; set; }
    public IList<SelectListItem> AvailableCategories { get; set; }
}

/// <summary>One row of the inventory list grid.</summary>
public partial record InventoryItemModel : BaseNopModel
{
    public int ProductId { get; set; }
    public int CombinationId { get; set; }
    public string VariantInfo { get; set; }
    public string Sku { get; set; }
    public string Ean { get; set; }
    public string ProductName { get; set; }
    public string ManufacturerName { get; set; }
    public string VendorName { get; set; }
    public string Price { get; set; }
    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public string Categories { get; set; }
    public string ProductEditUrl { get; set; }
}

/// <summary>Paged grid response for the inventory list.</summary>
public partial record InventoryGridListModel : BasePagedListModel<InventoryItemModel>;
