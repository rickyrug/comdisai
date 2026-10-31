using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.ViewModels.PurchaseOrders;

public class PurchaseOrderEditViewModel : PurchaseOrderHeaderInputViewModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public string? ReceivedBy { get; set; }
    public List<PurchaseOrderDetailInputViewModel> Details { get; set; } = [];
    public IReadOnlyList<SelectListItem> Products { get; set; } = [];
}