using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class SalesOrderDetail : AuditableEntity
{
    public int Id { get; set; }

    public int SalesOrderHeaderId { get; set; }
    public SalesOrderHeader SalesOrderHeader { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal? DiscountPercent { get; set; }
    public decimal Qty { get; set; }
    public decimal Price { get; set; }
    public decimal SubTotal { get; set; }
}
