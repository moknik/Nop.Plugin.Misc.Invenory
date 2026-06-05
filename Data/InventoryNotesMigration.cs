using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.Inventory.Domain;

namespace Nop.Plugin.Misc.Inventory.Data;

/// <summary>
/// Adds the <see cref="InventorySession.Notes"/> column to existing installs. On a fresh install the
/// table is created with the column already present (<see cref="InventorySchemaMigration"/>), so this
/// only fires where the table predates the notes feature.
/// </summary>
/// <remarks>
/// Deliberately a <see cref="MigrationProcessType.NoMatter"/> migration (the default), NOT an Update
/// one: on startup nopCommerce commits Update-type plugin migrations as applied <em>without running
/// them</em> (BaseDataProvider.InitializeDatabase), so an Update migration that changes schema never
/// actually executes. NoMatter migrations are left alone there and are run by the plugin update flow
/// (PluginService.InsertPluginData → ApplyUpMigrations) when plugin.json's version is bumped.
/// </remarks>
[NopMigration("2026/06/01 22:30:00", "Misc.Inventory session notes")]
public class InventoryNotesMigration : Migration
{
    public override void Up()
    {
        if (Schema.Table(nameof(InventorySession)).Exists()
            && !Schema.Table(nameof(InventorySession)).Column(nameof(InventorySession.Notes)).Exists())
        {
            Alter.Table(nameof(InventorySession))
                .AddColumn(nameof(InventorySession.Notes)).AsString(int.MaxValue).Nullable();
        }
    }

    public override void Down()
    {
        // Keep the column (and any notes) on rollback.
    }
}
