using ComdisAI.Data;
using ComdisAI.Models;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Services;

public sealed record OpenPurchaseOrderBalance(int OrderId, string OrderNumber, decimal Total, decimal Paid)
{
    public decimal Balance => Total - Paid;
}

public sealed class SupplierPaymentService(AppDbContext dbContext)
{
    private static readonly string[] AllocatableStatuses = ["APPROVED", "SUPPLIED"];

    public static decimal Allocated(SupplierPayment payment) =>
        payment.Allocations.Sum(allocation => allocation.Amount);

    public async Task<IReadOnlyList<OpenPurchaseOrderBalance>> GetOpenOrdersAsync(
        int supplierId,
        CancellationToken cancellationToken = default)
    {
        var orders = await dbContext.PurchaseOrderHeaders
            .AsNoTracking()
            .Include(order => order.Status)
            .Where(order => order.SupplierId == supplierId)
            .ToListAsync(cancellationToken);
        var paid = await GetPaidByOrderAsync(orders.Select(order => order.Id), cancellationToken);
        return orders
            .Where(IsAllocatable)
            .Select(order => new OpenPurchaseOrderBalance(
                order.Id, order.OrderNumber, order.Total, paid.GetValueOrDefault(order.Id)))
            .Where(balance => balance.Balance > 0m)
            .OrderBy(balance => balance.OrderNumber)
            .ToList();
    }

    public async Task<Dictionary<int, decimal>> GetPaidByOrderAsync(
        IEnumerable<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        var ids = orderIds.ToList();
        return (await dbContext.SupplierPaymentAllocations
                .AsNoTracking()
                .Where(allocation => ids.Contains(allocation.PurchaseOrderHeaderId))
                .Select(allocation => new { allocation.PurchaseOrderHeaderId, allocation.Amount })
                .ToListAsync(cancellationToken))
            .GroupBy(allocation => allocation.PurchaseOrderHeaderId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
    }

    public async Task<string?> AllocateAsync(
        int paymentId,
        int orderId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        amount = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
            return "The allocation amount must be greater than zero.";

        var payment = await dbContext.SupplierPayments
            .Include(row => row.Allocations)
            .SingleOrDefaultAsync(row => row.Id == paymentId, cancellationToken);
        if (payment is null)
            return "The payment was not found.";

        var order = await dbContext.PurchaseOrderHeaders
            .Include(row => row.Status)
            .SingleOrDefaultAsync(row => row.Id == orderId, cancellationToken);
        if (order is null || order.SupplierId != payment.SupplierId)
            return "Select a purchase order that belongs to the payment's supplier.";
        if (!IsAllocatable(order))
            return "Only approved or supplied purchase orders can receive payments.";

        var unallocated = payment.Amount - Allocated(payment);
        if (amount > unallocated)
            return $"The amount exceeds the unallocated payment balance ({unallocated:0.00##}).";

        var paid = (await GetPaidByOrderAsync([orderId], cancellationToken)).GetValueOrDefault(orderId);
        var outstanding = order.Total - paid;
        if (amount > outstanding)
            return $"The amount exceeds the order's outstanding balance ({outstanding:0.00##}).";

        var existing = payment.Allocations.FirstOrDefault(row => row.PurchaseOrderHeaderId == orderId);
        if (existing is null)
        {
            dbContext.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
            {
                SupplierPaymentId = paymentId,
                PurchaseOrderHeaderId = orderId,
                Amount = amount
            });
        }
        else
        {
            existing.Amount += amount;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public static string? ValidateAmountChange(SupplierPayment payment, decimal newAmount) =>
        newAmount < Allocated(payment)
            ? $"The amount cannot be lower than what is already allocated ({Allocated(payment):0.00##})."
            : null;

    private static bool IsAllocatable(PurchaseOrderHeader order) =>
        AllocatableStatuses.Contains(order.Status.Name.Trim().ToUpperInvariant());
}
