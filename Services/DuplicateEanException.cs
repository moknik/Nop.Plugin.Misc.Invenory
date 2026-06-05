namespace Nop.Plugin.Misc.Inventory.Services;

/// <summary>
/// Raised when an EAN/GTIN scan (or SKU/EAN lookup) matches more than one catalog product, so it can't be
/// resolved to a single product. The controller turns this into a distinct <c>duplicate</c> response flag
/// so the scan screen can show a duplicate-specific warning instead of the generic "not found" message.
/// </summary>
public class DuplicateEanException : Exception
{
    public DuplicateEanException(string message) : base(message)
    {
    }
}
