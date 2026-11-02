namespace ComdisAI.Models;

public class SupplierPaymentAllocation : AuditableEntity
{
    public int Id { get; set; }

    public int SupplierPaymentId { get; set; }
    public SupplierPayment SupplierPayment { get; set; } = null!;

    public int PurchaseOrderHeaderId { get; set; }
    public PurchaseOrderHeader PurchaseOrderHeader { get; set; } = null!;

    public decimal Amount { get; set; }
}
