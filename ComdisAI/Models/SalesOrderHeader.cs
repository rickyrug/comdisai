using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class SalesOrderHeader : AuditableEntity
{
    public int Id { get; set; }

    [Required]
    [StringLength(10)]
    public string OrderNumber { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    [Required]
    [StringLength(359)]
    public string DeliveryAddress { get; set; } = string.Empty;

    public DateTime DeliveryDate { get; set; }

    public int StatusId { get; set; }
    public Status Status { get; set; } = null!;

    public int TaxId { get; set; }
    public Tax Tax { get; set; } = null!;

    public decimal DiscountPercent1 { get; set; }
    public decimal DiscountPercent2 { get; set; }
    public decimal DiscountPercent3 { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }

    public ICollection<SalesOrderDetail> Details { get; set; } = [];
}
