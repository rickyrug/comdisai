using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.ViewModels.SalesOrders;

public class SalesOrderEditViewModel : SalesOrderHeaderInputViewModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public List<SalesOrderDetailInputViewModel> Details { get; set; } = [];
    public IReadOnlyList<SelectListItem> Products { get; set; } = [];
}
