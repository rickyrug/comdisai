using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class SupplierPayment : AuditableEntity
{
    public int Id { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }

    [StringLength(100)]
    public string? Reference { get; set; }

    public int? BankId { get; set; }
    public Bank? Bank { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }

    public ICollection<SupplierPaymentAllocation> Allocations { get; set; } = [];
}
