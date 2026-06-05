using Nop.Core;

namespace Nop.Plugin.Misc.Inventory.Domain;

/// <summary>
/// One product within an <see cref="InventorySession"/>. <see cref="SnapshotStock"/> /
/// <see cref="SnapshotReserved"/> are captured at session start and never change; the picker
/// fills in <see cref="CountedStock"/> + <see cref="CountedAtUtc"/> via the OK / save-count
/// buttons. "Just record" semantics — the real W1 stock is never touched.
/// </summary>
public class InventorySessionItem : BaseEntity
{
    public int SessionId { get; set; }
    public int ProductId { get; set; }

    /// <summary>
    /// The product-attribute combination this row counts, when the product manages stock by attributes
    /// (<see cref="Nop.Core.Domain.Catalog.ManageInventoryMethod.ManageStockByAttributes"/>). <c>0</c> means
    /// the row counts the product itself (no combination). A product may therefore have several rows.
    /// </summary>
    public int CombinationId { get; set; }

    public int SnapshotStock { get; set; }
    public int SnapshotReserved { get; set; }
    public int? CountedStock { get; set; }
    public DateTime? CountedAtUtc { get; set; }
    public bool IsConfirmed { get; set; }
}
