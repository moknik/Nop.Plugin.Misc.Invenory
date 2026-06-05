using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.Inventory.Domain;

namespace Nop.Plugin.Misc.Inventory.Data;

/// <summary>
/// Creates the inventory-session tables — but only if they don't already exist. The tables share
/// their names with the ones the Catalog review plugin used to own (entity class names are
/// unchanged), so on an existing install this is a no-op and the historical session data is kept;
/// on a fresh install it creates them.
/// </summary>
/// <remarks>
/// A <see cref="MigrationProcessType.NoMatter"/> migration (the default), NOT an Update one. An
/// Update-type migration is filtered out during a fresh plugin install and only committed-as-applied
/// (without running its <c>Up()</c>) on startup — so on a clean database the tables would never be
/// created. NoMatter runs its <c>Up()</c> during the install flow; the <c>Exists()</c> guard keeps it
/// a no-op on installs where the tables already exist. See <see cref="InventoryNotesMigration"/>.
/// </remarks>
[NopMigration("2026/06/01 20:00:00", "Misc.Inventory session schema")]
public class InventorySchemaMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(InventorySession)).Exists())
            Create.TableFor<InventorySession>();
        if (!Schema.Table(nameof(InventorySessionItem)).Exists())
            Create.TableFor<InventorySessionItem>();
    }

    public override void Down()
    {
        // Keep the tables (and their data) on rollback — losing counted inventories is worse than
        // an orphan table if the plugin is uninstalled.
    }
}
