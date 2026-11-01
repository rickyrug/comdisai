using ComdisAI.Models;

namespace ComdisAI.ViewModels.SupplierPriceLists;

public class SupplierPriceListIndexViewModel
{
    public required Supplier Supplier { get; set; }
    public IReadOnlyList<SupplierPriceList> Items { get; set; } = [];
    public IReadOnlyDictionary<string, string> UserNames { get; set; } = new Dictionary<string, string>();

    public string UserName(string actorId) =>
        UserNames.TryGetValue(actorId, out var name) ? name : actorId;
}