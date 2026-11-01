using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class CustomerPriceList : AuditableEntity
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    [Range(typeof(decimal), "0", "99999999999999.9999", ErrorMessage = "Price must be zero or greater.")]
    public decimal Price { get; set; }
}
