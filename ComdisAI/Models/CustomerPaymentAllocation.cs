namespace ComdisAI.Models;

public class CustomerPaymentAllocation : AuditableEntity
{
    public int Id { get; set; }

    public int CustomerPaymentId { get; set; }
    public CustomerPayment CustomerPayment { get; set; } = null!;

    public int SalesOrderHeaderId { get; set; }
    public SalesOrderHeader SalesOrderHeader { get; set; } = null!;

    public decimal Amount { get; set; }
}
