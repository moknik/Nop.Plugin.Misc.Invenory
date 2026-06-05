using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.Inventory;

/// <summary>
/// Inventory — a read-only internal-warehouse stock list plus inventory sessions (snapshot, barcode
/// scanning, per-product counting). Split out of Catalog review so the picker's page is fast.
/// </summary>
public class InventoryPlugin : BasePlugin, IMiscPlugin
{
    private readonly ILocalizationService _localizationService;
    private readonly ILanguageService _languageService;
    private readonly IWebHelper _webHelper;
    private readonly ICustomerService _customerService;

    public InventoryPlugin(
        ILocalizationService localizationService,
        ILanguageService languageService,
        IWebHelper webHelper,
        ICustomerService customerService)
    {
        _localizationService = localizationService;
        _languageService = languageService;
        _webHelper = webHelper;
        _customerService = customerService;
    }

    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/Inventory/Index";
    }

    public override async Task InstallAsync()
    {
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.Moknik.Inventory"] = "Inventory check",
            ["Plugins.Misc.Moknik.Inventory.Description"] = "Stock-taking inventory — a warehouse stock list plus inventory sessions (snapshot, barcode scanning and per-product counting), a completed-inventories history and a one-shot stock correction with stock-history movements. Supports selecting the warehouse, including a virtual 'no warehouse' mode for products that track stock directly.",
            ["Plugins.Misc.Moknik.Inventory.Menu"] = "Inventory check",
            ["Plugins.Misc.Moknik.Inventory.Tab.Inventory"] = "Inventory check",
            ["Plugins.Misc.Moknik.Inventory.Tab.Selection"] = "Inventory selection",
            ["Plugins.Misc.Moknik.Inventory.Column.ProductId"] = "Id",
            ["Plugins.Misc.Moknik.Inventory.Column.Sku"] = "SKU",
            ["Plugins.Misc.Moknik.Inventory.Column.Ean"] = "EAN",
            ["Plugins.Misc.Moknik.Inventory.Column.Product"] = "Product",
            ["Plugins.Misc.Moknik.Inventory.Column.Manufacturer"] = "Manufacturer",
            ["Plugins.Misc.Moknik.Inventory.Column.Vendor"] = "Vendor",
            ["Plugins.Misc.Moknik.Inventory.Column.Price"] = "Price",
            ["Plugins.Misc.Moknik.Inventory.Column.Categories"] = "Categories",
            ["Plugins.Misc.Moknik.Inventory.Variant"] = "Variant",
            ["Plugins.Misc.Moknik.Inventory.Column.Stock"] = "Stock",
            ["Plugins.Misc.Moknik.Inventory.Column.Reserved"] = "Reserved",
            ["Plugins.Misc.Moknik.Inventory.Filter.Warehouse"] = "Warehouse",
            ["Plugins.Misc.Moknik.Inventory.Warehouse.None"] = "No warehouse (direct stock)",
            ["Plugins.Misc.Moknik.Inventory.Filter.Manufacturer"] = "Manufacturer",
            ["Plugins.Misc.Moknik.Inventory.Filter.Vendor"] = "Vendor",
            ["Plugins.Misc.Moknik.Inventory.Filter.Category"] = "Category",
            ["Plugins.Misc.Moknik.Inventory.Filter.Price"] = "Price",
            ["Plugins.Misc.Moknik.Inventory.Filter.Stock"] = "Stock",
            ["Plugins.Misc.Moknik.Inventory.Filter.From"] = "from",
            ["Plugins.Misc.Moknik.Inventory.Filter.To"] = "to",
            ["Plugins.Misc.Moknik.Inventory.Filter.OnlyReserved"] = "Only reserved",
            ["Plugins.Misc.Moknik.Inventory.Filter.Sample"] = "Random sample",
            ["Plugins.Misc.Moknik.Inventory.Filter.SampleHint"] = "Keep only N randomly chosen products (cycle counting).",
            ["Plugins.Misc.Moknik.Inventory.Filter.SortBy"] = "Sort by",
            ["Plugins.Misc.Moknik.Inventory.Sort.category"] = "Category",
            ["Plugins.Misc.Moknik.Inventory.Sort.name"] = "Name",
            ["Plugins.Misc.Moknik.Inventory.Sort.sku"] = "SKU",
            ["Plugins.Misc.Moknik.Inventory.Sort.stock"] = "Stock ↑",
            ["Plugins.Misc.Moknik.Inventory.Sort.stockDesc"] = "Stock ↓",
            ["Plugins.Misc.Moknik.Inventory.Sort.price"] = "Price ↑",
            ["Plugins.Misc.Moknik.Inventory.Sort.priceDesc"] = "Price ↓",
            ["Plugins.Misc.Moknik.Inventory.Sort.manufacturer"] = "Manufacturer",
            ["Plugins.Misc.Moknik.Inventory.Sort.vendor"] = "Vendor",
            ["Plugins.Misc.Moknik.Inventory.Filter.Any"] = "(any)",
            ["Plugins.Misc.Moknik.Inventory.Filter.Apply"] = "Apply",
            ["Plugins.Misc.Moknik.Inventory.Filter.Reset"] = "Reset",
            ["Plugins.Misc.Moknik.Inventory.Filter.Search"] = "Search",
            ["Plugins.Misc.Moknik.Inventory.Filter.SearchPlaceholder"] = "SKU / EAN / name",
            ["Plugins.Misc.Moknik.Inventory.Action.OpenProduct"] = "Open",
            ["Plugins.Misc.Moknik.Inventory.Action.Refresh"] = "Refresh",
            ["Plugins.Misc.Moknik.Inventory.Action.Print"] = "Print",
            ["Plugins.Misc.Moknik.Inventory.Action.Export"] = "Export",
            ["Plugins.Misc.Moknik.Inventory.ImportCounts"] = "Import counts",
            ["Plugins.Misc.Moknik.Inventory.ImportCountsHint"] = "Upload a previously exported .xlsx with the Counted column filled in (offline counting).",
            ["Plugins.Misc.Moknik.Inventory.PrintRowCountLabel"] = "rows",
            ["Plugins.Misc.Moknik.Inventory.Empty"] = "No products in stock on the selected warehouse.",
            ["Plugins.Misc.Moknik.Inventory.StartButton"] = "Start inventory",
            ["Plugins.Misc.Moknik.Inventory.CancelButton"] = "Delete inventory",
            ["Plugins.Misc.Moknik.Inventory.CancelConfirm"] = "Delete the active inventory? All counts will be lost.",
            ["Plugins.Misc.Moknik.Inventory.SessionTabTitle"] = "Inventory {0}",
            ["Plugins.Misc.Moknik.Inventory.SessionActiveHint"] = "Inventory in progress — switch to the inventory tab.",
            ["Plugins.Misc.Moknik.Inventory.SessionCapHint"] = "Maximum number of inventories ({0}) reached.",
            ["Plugins.Misc.Moknik.Inventory.Notes"] = "Notes",
            ["Plugins.Misc.Moknik.Inventory.NotesPlaceholder"] = "Notes for this inventory…",
            ["Plugins.Misc.Moknik.Inventory.NotesSave"] = "Save notes",
            ["Plugins.Misc.Moknik.Inventory.NotesSaved"] = "Saved",
            ["Plugins.Misc.Moknik.Inventory.CountedBy"] = "Counted by",
            ["Plugins.Misc.Moknik.Inventory.CountedByPlaceholder"] = "Name of the person who did the inventory",
            ["Plugins.Misc.Moknik.Inventory.SessionEmpty"] = "No items in the snapshot — no positive-stock products on the selected warehouse.",
            ["Plugins.Misc.Moknik.Inventory.StartedAt"] = "Started",
            ["Plugins.Misc.Moknik.Inventory.SnapshotStock"] = "Snapshot stock",
            ["Plugins.Misc.Moknik.Inventory.CountedStock"] = "Counted",
            ["Plugins.Misc.Moknik.Inventory.Actions"] = "Actions",
            ["Plugins.Misc.Moknik.Inventory.OkButton"] = "OK",
            ["Plugins.Misc.Moknik.Inventory.OkTooltip"] = "Count matches the snapshot",
            ["Plugins.Misc.Moknik.Inventory.SaveCountButton"] = "Save",
            ["Plugins.Misc.Moknik.Inventory.ScanEan"] = "Scan Barcode",
            ["Plugins.Misc.Moknik.Inventory.ScanEanPlaceholder"] = "Scan by barcode reader…",
            ["Plugins.Misc.Moknik.Inventory.ScanOk"] = "EAN {0}: counted = {1}",
            ["Plugins.Misc.Moknik.Inventory.ScanNotFound"] = "EAN {0} not in this inventory",
            ["Plugins.Misc.Moknik.Inventory.ScanDuplicate"] = "Duplicate EAN {0} – can't unambiguously match a product",
            ["Plugins.Misc.Moknik.Inventory.DuplicateEanBadge"] = "duplicate EAN",
            ["Plugins.Misc.Moknik.Inventory.DuplicateEanTitle"] = "This EAN/GTIN is shared by more than one product — scanning it is blocked. Count these products by hand.",
            ["Plugins.Misc.Moknik.Inventory.StatusFilter"] = "Status",
            ["Plugins.Misc.Moknik.Inventory.StatusDone"] = "Done",
            ["Plugins.Misc.Moknik.Inventory.StatusDiff"] = "Mismatch",
            ["Plugins.Misc.Moknik.Inventory.StatusPending"] = "Not counted",
            ["Plugins.Misc.Moknik.Inventory.AddProduct"] = "Add product",
            ["Plugins.Misc.Moknik.Inventory.AddProductPlaceholder"] = "SKU / EAN",
            ["Plugins.Misc.Moknik.Inventory.AddProductCount"] = "Qty",
            ["Plugins.Misc.Moknik.Inventory.AddProductButton"] = "Add",
            ["Plugins.Misc.Moknik.Inventory.AddProductHint"] = "A product found in the warehouse but not in stock — added as a mismatch.",
            ["Plugins.Misc.Moknik.Inventory.AddProductNotFound"] = "Product {0} not found",
            ["Plugins.Misc.Moknik.Inventory.AddProductAdded"] = "Added: {0}",
            ["Plugins.Misc.Moknik.Inventory.FinishButton"] = "Finish inventory",
            ["Plugins.Misc.Moknik.Inventory.Report.Title"] = "Inventory report",
            ["Plugins.Misc.Moknik.Inventory.Report.GeneratedAt"] = "Generated",
            ["Plugins.Misc.Moknik.Inventory.Report.DiffHeading"] = "Mismatched items",
            ["Plugins.Misc.Moknik.Inventory.Report.PendingHeading"] = "Not counted items",
            ["Plugins.Misc.Moknik.Inventory.Report.NoDiff"] = "No mismatches.",
            ["Plugins.Misc.Moknik.Inventory.Report.NoPending"] = "All items counted.",
            ["Plugins.Misc.Moknik.Inventory.Report.Difference"] = "Difference",
            ["Plugins.Misc.Moknik.Inventory.Report.DiffValue"] = "Difference value",
            ["Plugins.Misc.Moknik.Inventory.Report.Signature"] = "Signature",
            ["Plugins.Misc.Moknik.Inventory.Report.SignedBy"] = "Counted by",
            ["Plugins.Misc.Moknik.Inventory.FinishConfirm"] = "Finish and lock the inventory? It can no longer be edited.",
            ["Plugins.Misc.Moknik.Inventory.StockHistoryMessage"] = "Change after inventory {0}",
            ["Plugins.Misc.Moknik.Inventory.Tab.Completed"] = "Completed inventories",
            ["Plugins.Misc.Moknik.Inventory.Completed.Empty"] = "No completed inventories yet.",
            ["Plugins.Misc.Moknik.Inventory.Completed.CompletedAt"] = "Completed",
            ["Plugins.Misc.Moknik.Inventory.Completed.Items"] = "Items",
            ["Plugins.Misc.Moknik.Inventory.Completed.Mismatches"] = "Mismatches",
            ["Plugins.Misc.Moknik.Inventory.Completed.Pending"] = "Not counted",
            ["Plugins.Misc.Moknik.Inventory.Completed.Value"] = "Difference value",
            ["Plugins.Misc.Moknik.Inventory.Completed.Shown"] = "Shown",
            ["Plugins.Misc.Moknik.Inventory.Completed.StockStatus"] = "Stock",
            ["Plugins.Misc.Moknik.Inventory.Completed.StockApplied"] = "Stocked {0}",
            ["Plugins.Misc.Moknik.Inventory.Completed.StockPending"] = "Not stocked",
            ["Plugins.Misc.Moknik.Inventory.Completed.StockNone"] = "No differences",
            ["Plugins.Misc.Moknik.Inventory.Completed.ApplyStockButton"] = "Apply stock",
            ["Plugins.Misc.Moknik.Inventory.Completed.ApplyStockConfirm"] = "Stock-in / stock-out the differences to the selected warehouse? This can be done only once.",
            ["Plugins.Misc.Moknik.Inventory.Completed.DeleteButton"] = "Delete",
            ["Plugins.Misc.Moknik.Inventory.Completed.DeleteConfirm"] = "Delete this completed inventory? Stock movements already made are kept."
        });

        var czechLanguage = (await _languageService.GetAllLanguagesAsync(showHidden: true))
            .FirstOrDefault(l =>
                string.Equals(l.LanguageCulture, "cs-CZ", StringComparison.OrdinalIgnoreCase) ||
                l.LanguageCulture?.StartsWith("cs", StringComparison.OrdinalIgnoreCase) == true);

        if (czechLanguage != null)
        {
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Misc.Moknik.Inventory"] = "Inventura",
                ["Plugins.Misc.Moknik.Inventory.Description"] = "Inventura skladu — přehled skladových zásob a inventurní relace (snímek stavu, načítání čárových kódů a počítání kusů po produktech), historie dokončených inventur a jednorázová korekce stavu zásob se zápisem do historie skladových pohybů. Umožňuje výběr skladu včetně virtuálního režimu „bez skladu“ pro produkty se zásobou vedenou přímo.",
                ["Plugins.Misc.Moknik.Inventory.Menu"] = "Inventura",
                ["Plugins.Misc.Moknik.Inventory.Tab.Inventory"] = "Inventura",
                ["Plugins.Misc.Moknik.Inventory.Tab.Selection"] = "Výběr inventury",
                ["Plugins.Misc.Moknik.Inventory.Column.ProductId"] = "Id",
                ["Plugins.Misc.Moknik.Inventory.Column.Sku"] = "SKU",
                ["Plugins.Misc.Moknik.Inventory.Column.Ean"] = "EAN",
                ["Plugins.Misc.Moknik.Inventory.Column.Product"] = "Produkt",
                ["Plugins.Misc.Moknik.Inventory.Column.Manufacturer"] = "Výrobce",
                ["Plugins.Misc.Moknik.Inventory.Column.Vendor"] = "Dodavatel",
                ["Plugins.Misc.Moknik.Inventory.Column.Price"] = "Cena",
                ["Plugins.Misc.Moknik.Inventory.Column.Categories"] = "Kategorie",
                ["Plugins.Misc.Moknik.Inventory.Variant"] = "Varianta",
                ["Plugins.Misc.Moknik.Inventory.Column.Stock"] = "Skladem",
                ["Plugins.Misc.Moknik.Inventory.Column.Reserved"] = "Rezervováno",
                ["Plugins.Misc.Moknik.Inventory.Filter.Warehouse"] = "Sklad",
                ["Plugins.Misc.Moknik.Inventory.Warehouse.None"] = "Bez skladu (přímá zásoba)",
                ["Plugins.Misc.Moknik.Inventory.Filter.Manufacturer"] = "Výrobce",
                ["Plugins.Misc.Moknik.Inventory.Filter.Vendor"] = "Dodavatel",
                ["Plugins.Misc.Moknik.Inventory.Filter.Category"] = "Kategorie",
                ["Plugins.Misc.Moknik.Inventory.Filter.Price"] = "Cena",
                ["Plugins.Misc.Moknik.Inventory.Filter.Stock"] = "Skladem",
                ["Plugins.Misc.Moknik.Inventory.Filter.From"] = "od",
                ["Plugins.Misc.Moknik.Inventory.Filter.To"] = "do",
                ["Plugins.Misc.Moknik.Inventory.Filter.OnlyReserved"] = "Jen s rezervací",
                ["Plugins.Misc.Moknik.Inventory.Filter.Sample"] = "Náhodný vzorek",
                ["Plugins.Misc.Moknik.Inventory.Filter.SampleHint"] = "Ponechat jen N náhodně vybraných produktů (cyklická inventura).",
                ["Plugins.Misc.Moknik.Inventory.Filter.SortBy"] = "Řadit dle",
                ["Plugins.Misc.Moknik.Inventory.Sort.category"] = "Kategorie",
                ["Plugins.Misc.Moknik.Inventory.Sort.name"] = "Název",
                ["Plugins.Misc.Moknik.Inventory.Sort.sku"] = "SKU",
                ["Plugins.Misc.Moknik.Inventory.Sort.stock"] = "Skladem ↑",
                ["Plugins.Misc.Moknik.Inventory.Sort.stockDesc"] = "Skladem ↓",
                ["Plugins.Misc.Moknik.Inventory.Sort.price"] = "Cena ↑",
                ["Plugins.Misc.Moknik.Inventory.Sort.priceDesc"] = "Cena ↓",
                ["Plugins.Misc.Moknik.Inventory.Sort.manufacturer"] = "Výrobce",
                ["Plugins.Misc.Moknik.Inventory.Sort.vendor"] = "Dodavatel",
                ["Plugins.Misc.Moknik.Inventory.Filter.Any"] = "(jakýkoli)",
                ["Plugins.Misc.Moknik.Inventory.Filter.Apply"] = "Použít",
                ["Plugins.Misc.Moknik.Inventory.Filter.Reset"] = "Reset",
                ["Plugins.Misc.Moknik.Inventory.Filter.Search"] = "Hledat",
                ["Plugins.Misc.Moknik.Inventory.Filter.SearchPlaceholder"] = "SKU / EAN / název",
                ["Plugins.Misc.Moknik.Inventory.Action.OpenProduct"] = "Otevřít",
                ["Plugins.Misc.Moknik.Inventory.Action.Refresh"] = "Obnovit",
                ["Plugins.Misc.Moknik.Inventory.Action.Print"] = "Tisk",
                ["Plugins.Misc.Moknik.Inventory.Action.Export"] = "Export",
                ["Plugins.Misc.Moknik.Inventory.ImportCounts"] = "Importovat počty",
                ["Plugins.Misc.Moknik.Inventory.ImportCountsHint"] = "Nahraj dříve exportovaný .xlsx s vyplněným sloupcem Spočítáno (offline počítání).",
                ["Plugins.Misc.Moknik.Inventory.PrintRowCountLabel"] = "položek",
                ["Plugins.Misc.Moknik.Inventory.Empty"] = "Na vybraném skladě nejsou žádné skladové produkty.",
                ["Plugins.Misc.Moknik.Inventory.StartButton"] = "Začít inventuru",
                ["Plugins.Misc.Moknik.Inventory.CancelButton"] = "Smazat inventuru",
                ["Plugins.Misc.Moknik.Inventory.CancelConfirm"] = "Smazat aktivní inventuru? Veškerá data se ztratí.",
                ["Plugins.Misc.Moknik.Inventory.SessionTabTitle"] = "Inventura {0}",
                ["Plugins.Misc.Moknik.Inventory.SessionActiveHint"] = "Inventura již probíhá — přepněte na záložku inventury.",
                ["Plugins.Misc.Moknik.Inventory.SessionCapHint"] = "Dosažen maximální počet inventur ({0}).",
                ["Plugins.Misc.Moknik.Inventory.Notes"] = "Poznámky",
                ["Plugins.Misc.Moknik.Inventory.NotesPlaceholder"] = "Poznámky k této inventuře…",
                ["Plugins.Misc.Moknik.Inventory.NotesSave"] = "Uložit poznámky",
                ["Plugins.Misc.Moknik.Inventory.NotesSaved"] = "Uloženo",
                ["Plugins.Misc.Moknik.Inventory.CountedBy"] = "Inventuru provedl",
                ["Plugins.Misc.Moknik.Inventory.CountedByPlaceholder"] = "Jméno osoby, která provedla inventuru",
                ["Plugins.Misc.Moknik.Inventory.SessionEmpty"] = "Snapshot je prázdný — na vybraném skladě nejsou žádné položky s kladnou zásobou.",
                ["Plugins.Misc.Moknik.Inventory.StartedAt"] = "Začátek",
                ["Plugins.Misc.Moknik.Inventory.SnapshotStock"] = "Stav v systému",
                ["Plugins.Misc.Moknik.Inventory.CountedStock"] = "Spočítáno",
                ["Plugins.Misc.Moknik.Inventory.Actions"] = "Akce",
                ["Plugins.Misc.Moknik.Inventory.OkButton"] = "OK",
                ["Plugins.Misc.Moknik.Inventory.OkTooltip"] = "Sedí počet kusů",
                ["Plugins.Misc.Moknik.Inventory.SaveCountButton"] = "Uložit",
                ["Plugins.Misc.Moknik.Inventory.ScanEan"] = "Načíst čárový kód",
                ["Plugins.Misc.Moknik.Inventory.ScanEanPlaceholder"] = "Naskenuj čtečkou čárových kódů…",
                ["Plugins.Misc.Moknik.Inventory.ScanOk"] = "EAN {0}: spočítáno = {1}",
                ["Plugins.Misc.Moknik.Inventory.ScanNotFound"] = "EAN {0} v této inventuře není",
                ["Plugins.Misc.Moknik.Inventory.ScanDuplicate"] = "Duplicitní EAN {0} – nelze jednoznačně přiřadit produkt",
                ["Plugins.Misc.Moknik.Inventory.DuplicateEanBadge"] = "duplicitní EAN",
                ["Plugins.Misc.Moknik.Inventory.DuplicateEanTitle"] = "Tento EAN/GTIN sdílí více produktů – skenování je zablokované. Tyto produkty spočítejte ručně.",
                ["Plugins.Misc.Moknik.Inventory.StatusFilter"] = "Stav",
                ["Plugins.Misc.Moknik.Inventory.StatusDone"] = "Hotovo",
                ["Plugins.Misc.Moknik.Inventory.StatusDiff"] = "Rozdíl",
                ["Plugins.Misc.Moknik.Inventory.StatusPending"] = "Nehotové",
                ["Plugins.Misc.Moknik.Inventory.AddProduct"] = "Přidat produkt",
                ["Plugins.Misc.Moknik.Inventory.AddProductPlaceholder"] = "SKU / EAN",
                ["Plugins.Misc.Moknik.Inventory.AddProductCount"] = "Počet",
                ["Plugins.Misc.Moknik.Inventory.AddProductButton"] = "Přidat",
                ["Plugins.Misc.Moknik.Inventory.AddProductHint"] = "Produkt nalezený na skladě, ale není naskladněný — přidá se jako rozdílný.",
                ["Plugins.Misc.Moknik.Inventory.AddProductNotFound"] = "Produkt {0} nenalezen",
                ["Plugins.Misc.Moknik.Inventory.AddProductAdded"] = "Přidáno: {0}",
                ["Plugins.Misc.Moknik.Inventory.FinishButton"] = "Dokončit inventuru",
                ["Plugins.Misc.Moknik.Inventory.Report.Title"] = "Protokol o inventuře",
                ["Plugins.Misc.Moknik.Inventory.Report.GeneratedAt"] = "Vygenerováno",
                ["Plugins.Misc.Moknik.Inventory.Report.DiffHeading"] = "Rozdílné položky",
                ["Plugins.Misc.Moknik.Inventory.Report.PendingHeading"] = "Nehotové položky",
                ["Plugins.Misc.Moknik.Inventory.Report.NoDiff"] = "Žádné rozdíly.",
                ["Plugins.Misc.Moknik.Inventory.Report.NoPending"] = "Vše spočítáno.",
                ["Plugins.Misc.Moknik.Inventory.Report.Difference"] = "Rozdíl",
                ["Plugins.Misc.Moknik.Inventory.Report.DiffValue"] = "Hodnota rozdílů",
                ["Plugins.Misc.Moknik.Inventory.Report.Signature"] = "Podpis",
                ["Plugins.Misc.Moknik.Inventory.Report.SignedBy"] = "Inventuru provedl",
                ["Plugins.Misc.Moknik.Inventory.FinishConfirm"] = "Dokončit a uzamknout inventuru? Už ji nepůjde upravovat.",
                ["Plugins.Misc.Moknik.Inventory.StockHistoryMessage"] = "Změna po inventuře {0}",
                ["Plugins.Misc.Moknik.Inventory.Tab.Completed"] = "Dokončené inventury",
                ["Plugins.Misc.Moknik.Inventory.Completed.Empty"] = "Zatím žádné dokončené inventury.",
                ["Plugins.Misc.Moknik.Inventory.Completed.CompletedAt"] = "Dokončeno",
                ["Plugins.Misc.Moknik.Inventory.Completed.Items"] = "Položek",
                ["Plugins.Misc.Moknik.Inventory.Completed.Mismatches"] = "Rozdílů",
                ["Plugins.Misc.Moknik.Inventory.Completed.Pending"] = "Nespočítáno",
                ["Plugins.Misc.Moknik.Inventory.Completed.Value"] = "Hodnota rozdílů",
                ["Plugins.Misc.Moknik.Inventory.Completed.Shown"] = "Zobrazeno",
                ["Plugins.Misc.Moknik.Inventory.Completed.StockStatus"] = "Sklad",
                ["Plugins.Misc.Moknik.Inventory.Completed.StockApplied"] = "Naskladněno {0}",
                ["Plugins.Misc.Moknik.Inventory.Completed.StockPending"] = "Nenaskladněno",
                ["Plugins.Misc.Moknik.Inventory.Completed.StockNone"] = "Bez rozdílů",
                ["Plugins.Misc.Moknik.Inventory.Completed.ApplyStockButton"] = "Naskladnit/odskladnit",
                ["Plugins.Misc.Moknik.Inventory.Completed.ApplyStockConfirm"] = "Naskladnit/odskladnit rozdíly na vybraný sklad? Tuto akci lze provést pouze jednou.",
                ["Plugins.Misc.Moknik.Inventory.Completed.DeleteButton"] = "Smazat",
                ["Plugins.Misc.Moknik.Inventory.Completed.DeleteConfirm"] = "Smazat tuto dokončenou inventuru? Již provedené skladové pohyby zůstanou zachovány."
            }, czechLanguage.Id);
        }

        await RestrictToAdministratorsByDefaultAsync();

        await base.InstallAsync();
    }

    /// <summary>
    /// Restricts the plugin to the Administrators customer role by default. Applied only on a fresh
    /// install (empty ACL) so it is not re-imposed on version-bump updates that re-run <see cref="InstallAsync"/>;
    /// the store owner can change it afterwards in the plugin's Edit dialog (Limited to customer roles).
    /// </summary>
    private async Task RestrictToAdministratorsByDefaultAsync()
    {
        if (PluginDescriptor.LimitedToCustomerRoles.Any())
            return;

        var adminRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.AdministratorsRoleName);
        if (adminRole == null)
            return;

        PluginDescriptor.LimitedToCustomerRoles = new List<int> { adminRole.Id };
        PluginDescriptor.Save();
    }

    public override async Task UninstallAsync()
    {
        await _localizationService.DeleteLocaleResourcesAsync(InventoryDefaults.ResourceKeyPrefix);
        await base.UninstallAsync();
    }

    /// <summary>Re-runs <see cref="InstallAsync"/> on version bumps so newly added locale keys land in existing databases.</summary>
    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await InstallAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }
}
