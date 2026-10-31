namespace ComdisAI.Models;

public class PurchaseOrderDetail : AuditableEntity
{
    public int Id { get; set; }

    public int PurchaseOrderHeaderId { get; set; }
    public PurchaseOrderHeader PurchaseOrderHeader { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal? DiscountPercent { get; set; }
    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public decimal SubTotal { get; set; }
}