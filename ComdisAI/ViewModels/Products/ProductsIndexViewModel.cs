using ComdisAI.Models;

namespace ComdisAI.ViewModels.Products;

public sealed class ProductsIndexViewModel
{
    public string SearchTerm { get; set; } = string.Empty;
    public IReadOnlyList<Product> Products { get; set; } = [];
}
