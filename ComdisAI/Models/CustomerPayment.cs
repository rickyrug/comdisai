using System.ComponentModel.DataAnnotations;

namespace ComdisAI.Models;

public class CustomerPayment : AuditableEntity
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

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

    public ICollection<CustomerPaymentAllocation> Allocations { get; set; } = [];
}
