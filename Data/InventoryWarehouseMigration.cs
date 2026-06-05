using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.Inventory.Domain;

namespace Nop.Plugin.Misc.Inventory.Data;

/// <summary>
/// Adds the <see cref="InventorySession.WarehouseId"/> column to existing installs (which warehouse the
/// inventory counts). Existing sessions default to <c>0</c> ("no warehouse" mode); that is fine — they
/// predate warehouse selection and any stock-apply for them already ran or is historical.
/// </summary>
/// <remarks>
/// A <see cref="MigrationProcessType.NoMatter"/> migration like the other column-adding ones, so it is
/// actually executed by the plugin update flow when plugin.json's version is bumped.
/// </remarks>
[NopMigration("2026/06/02 10:00:00", "Misc.Inventory session warehouse")]
public class InventoryWarehouseMigration : Migration
{
    public override void Up()
    {
        if (Schema.Table(nameof(InventorySession)).Exists()
            && !Schema.Table(nameof(InventorySession)).Column(nameof(InventorySession.WarehouseId)).Exists())
        {
            Alter.Table(nameof(InventorySession))
                .AddColumn(nameof(InventorySession.WarehouseId)).AsInt32().NotNullable().WithDefaultValue(0);
        }
    }

    public override void Down()
    {
        // Keep the column on rollback.
    }
}
