using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class SupplierPriceList : AuditableEntity
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    [Range(typeof(decimal), "0", "99999999999999.9999", ErrorMessage = "Price must be zero or greater.")]
    public decimal Price { get; set; }
}