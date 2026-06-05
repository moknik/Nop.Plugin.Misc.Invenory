using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.Inventory.Domain;

namespace Nop.Plugin.Misc.Inventory.Data;

/// <summary>
/// Adds the completion / stock-applied columns to <see cref="InventorySession"/> on existing installs
/// (<see cref="InventorySession.CompletedOnUtc"/>, <see cref="InventorySession.StockApplied"/>,
/// <see cref="InventorySession.StockAppliedOnUtc"/>). On a fresh install the table is created with the
/// columns already present (<see cref="InventorySchemaMigration"/>), so this only fires where the table
/// predates the finish-inventory feature.
/// </summary>
/// <remarks>
/// A <see cref="MigrationProcessType.NoMatter"/> migration (the default) like <see cref="InventoryNotesMigration"/>,
/// so it is actually executed by the plugin update flow (PluginService.InsertPluginData → ApplyUpMigrations)
/// when plugin.json's version is bumped — Update-type plugin migrations are committed as applied without
/// running on startup.
/// </remarks>
[NopMigration("2026/06/01 23:30:00", "Misc.Inventory session completion")]
public class InventoryCompletionMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(InventorySession)).Exists())
            return;

        if (!Schema.Table(nameof(InventorySession)).Column(nameof(InventorySession.CompletedOnUtc)).Exists())
            Alter.Table(nameof(InventorySession))
                .AddColumn(nameof(InventorySession.CompletedOnUtc)).AsDateTime2().Nullable();

        if (!Schema.Table(nameof(InventorySession)).Column(nameof(InventorySession.StockApplied)).Exists())
            Alter.Table(nameof(InventorySession))
                .AddColumn(nameof(InventorySession.StockApplied)).AsBoolean().NotNullable().WithDefaultValue(false);

        if (!Schema.Table(nameof(InventorySession)).Column(nameof(InventorySession.StockAppliedOnUtc)).Exists())
            Alter.Table(nameof(InventorySession))
                .AddColumn(nameof(InventorySession.StockAppliedOnUtc)).AsDateTime2().Nullable();
    }

    public override void Down()
    {
        // Keep the columns (and any completion data) on rollback.
    }
}
