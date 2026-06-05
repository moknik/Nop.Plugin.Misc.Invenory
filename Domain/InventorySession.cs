using Nop.Core;

namespace Nop.Plugin.Misc.Inventory.Domain;

/// <summary>
/// A stock-taking session — a snapshot of W1 stock at a point in time plus per-product counts
/// entered by the picker. Up to <see cref="InventoryDefaults.MaxActiveSessions"/> active sessions
/// can run at once (<see cref="IsActive"/>); cancelling deletes the session and all its items.
/// </summary>
public class InventorySession : BaseEntity
{
    public DateTime StartedOnUtc { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Free-form picker notes for this inventory (nullable).</summary>
    public string Notes { get; set; }

    /// <summary>Who carried out the inventory (nullable). When set, it is printed on the report's
    /// "counted by" line; otherwise that line is left blank.</summary>
    public string CountedBy { get; set; }

    /// <summary>
    /// The warehouse this inventory counts. A positive id is a real <c>Warehouse</c>; <c>0</c> is the
    /// virtual "no warehouse" mode (products that track stock directly via <c>Product.StockQuantity</c>
    /// with <c>UseMultipleWarehouses = false</c>). Drives both the snapshot and where the stock
    /// correction is written.
    /// </summary>
    public int WarehouseId { get; set; }

    /// <summary>
    /// When set, the inventory has been finished and locked — it can no longer be counted and moves to
    /// the "completed inventories" history. <see cref="IsActive"/> is cleared at the same time.
    /// </summary>
    public DateTime? CompletedOnUtc { get; set; }

    /// <summary>
    /// True once the one-shot "apply stock changes" action has run for this (completed) inventory — the
    /// W1 stock of the mismatched items has been corrected and stock-history movements written. Guards
    /// the action so it can only be applied once.
    /// </summary>
    public bool StockApplied { get; set; }

    /// <summary>When the stock changes were applied (nullable).</summary>
    public DateTime? StockAppliedOnUtc { get; set; }
}
