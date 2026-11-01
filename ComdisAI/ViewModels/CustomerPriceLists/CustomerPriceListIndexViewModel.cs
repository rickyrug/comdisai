using ComdisAI.Models;

namespace ComdisAI.ViewModels.CustomerPriceLists;

public class CustomerPriceListIndexViewModel
{
    public required Customer Customer { get; set; }
    public IReadOnlyList<CustomerPriceList> Items { get; set; } = [];
    public IReadOnlyDictionary<string, string> UserNames { get; set; } = new Dictionary<string, string>();

    public string UserName(string actorId) =>
        UserNames.TryGetValue(actorId, out var name) ? name : actorId;
}
