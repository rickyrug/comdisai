namespace ComdisAI.Models;

public class CustomerPriceHistory : AuditableEntity
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal? PreviousPrice { get; set; }
    public decimal NewPrice { get; set; }
}
