using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.Inventory.Domain;

namespace Nop.Plugin.Misc.Inventory.Data;

/// <summary>
/// Adds the <see cref="InventorySessionItem.CombinationId"/> column to existing installs (attribute-combination
/// inventory rows). Existing rows default to <c>0</c> (no combination — they count the product itself).
/// </summary>
/// <remarks>
/// A <see cref="MigrationProcessType.NoMatter"/> migration (the default) like the other column-adding ones —
/// see <see cref="InventoryNotesMigration"/> for why Update-type schema migrations don't actually run.
/// </remarks>
[NopMigration("2026/06/03 11:00:00", "Misc.Inventory session item combination")]
public class InventoryCombinationMigration : Migration
{
    public override void Up()
    {
        if (Schema.Table(nameof(InventorySessionItem)).Exists()
            && !Schema.Table(nameof(InventorySessionItem)).Column(nameof(InventorySessionItem.CombinationId)).Exists())
        {
            Alter.Table(nameof(InventorySessionItem))
                .AddColumn(nameof(InventorySessionItem.CombinationId)).AsInt32().NotNullable().WithDefaultValue(0);
        }
    }

    public override void Down()
    {
        // Keep the column on rollback.
    }
}
