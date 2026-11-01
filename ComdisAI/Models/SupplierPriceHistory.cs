namespace ComdisAI.Models;

public class SupplierPriceHistory : AuditableEntity
{
    public int Id { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal? PreviousPrice { get; set; }
    public decimal NewPrice { get; set; }
}
