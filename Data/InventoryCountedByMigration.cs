using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.Inventory.Domain;

namespace Nop.Plugin.Misc.Inventory.Data;

/// <summary>
/// Adds the <see cref="InventorySession.CountedBy"/> column to existing installs. On a fresh install the
/// table is created with the column already present (<see cref="InventorySchemaMigration"/>), so this only
/// fires where the table predates the "counted by" feature.
/// </summary>
/// <remarks>
/// A <see cref="MigrationProcessType.NoMatter"/> migration (the default), NOT an Update one — see
/// <see cref="InventoryNotesMigration"/> for why: Update-type schema migrations get committed as applied
/// without their <c>Up()</c> ever running, so they never actually add the column.
/// </remarks>
[NopMigration("2026/06/03 10:00:00", "Misc.Inventory session counted-by")]
public class InventoryCountedByMigration : Migration
{
    public override void Up()
    {
        if (Schema.Table(nameof(InventorySession)).Exists()
            && !Schema.Table(nameof(InventorySession)).Column(nameof(InventorySession.CountedBy)).Exists())
        {
            Alter.Table(nameof(InventorySession))
                .AddColumn(nameof(InventorySession.CountedBy)).AsString(int.MaxValue).Nullable();
        }
    }

    public override void Down()
    {
        // Keep the column (and any value) on rollback.
    }
}
