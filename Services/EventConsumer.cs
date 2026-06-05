using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Misc.Inventory.Services;

/// <summary>Inserts the "Inventory" entry under Catalog, after the built-in "Manufacturers" item.</summary>
public class EventConsumer : BaseAdminMenuCreatedEventConsumer
{
    private readonly ILocalizationService _localizationService;

    public EventConsumer(IPluginManager<IPlugin> pluginManager, ILocalizationService localizationService)
        : base(pluginManager)
    {
        _localizationService = localizationService;
    }

    protected override string PluginSystemName => InventoryDefaults.SystemName;
    protected override MenuItemInsertType InsertType => MenuItemInsertType.After;
    protected override string AfterMenuSystemName => "Manufacturers";

    protected override async Task<AdminMenuItem> GetAdminMenuItemAsync(IPlugin plugin)
    {
        var title = await _localizationService.GetResourceAsync($"{InventoryDefaults.ResourceKeyPrefix}.Menu");

        return new AdminMenuItem
        {
            SystemName = InventoryDefaults.MenuSystemName,
            Title = string.IsNullOrEmpty(title) ? "Inventory" : title,
            IconClass = "fas fa-boxes",
            Url = "/Admin/Inventory/Index"
        };
    }
}
