using ComdisAI.Data;
using ComdisAI.Models;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Services;

public sealed record OpenOrderBalance(int OrderId, string OrderNumber, decimal Total, decimal Paid)
{
    public decimal Balance => Total - Paid;
}

public sealed class CustomerPaymentService(AppDbContext dbContext)
{
    private static readonly string[] AllocatableStatuses = ["APPROVED", "DELIVERED"];

    public static decimal Allocated(CustomerPayment payment) =>
        payment.Allocations.Sum(allocation => allocation.Amount);

    public async Task<IReadOnlyList<OpenOrderBalance>> GetOpenOrdersAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await dbContext.SalesOrderHeaders
            .AsNoTracking()
            .Include(order => order.Status)
            .Where(order => order.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(order => order.Id).ToList();
        var paid = (await dbContext.CustomerPaymentAllocations
                .AsNoTracking()
                .Where(allocation => orderIds.Contains(allocation.SalesOrderHeaderId))
                .Select(allocation => new { allocation.SalesOrderHeaderId, allocation.Amount })
                .ToListAsync(cancellationToken))
            .GroupBy(allocation => allocation.SalesOrderHeaderId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        return orders
            .Where(order => IsAllocatable(order))
            .Select(order => new OpenOrderBalance(
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
        return (await dbContext.CustomerPaymentAllocations
                .AsNoTracking()
                .Where(allocation => ids.Contains(allocation.SalesOrderHeaderId))
                .Select(allocation => new { allocation.SalesOrderHeaderId, allocation.Amount })
                .ToListAsync(cancellationToken))
            .GroupBy(allocation => allocation.SalesOrderHeaderId)
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

        var payment = await dbContext.CustomerPayments
            .Include(row => row.Allocations)
            .SingleOrDefaultAsync(row => row.Id == paymentId, cancellationToken);
        if (payment is null)
            return "The payment was not found.";

        var order = await dbContext.SalesOrderHeaders
            .Include(row => row.Status)
            .SingleOrDefaultAsync(row => row.Id == orderId, cancellationToken);
        if (order is null || order.CustomerId != payment.CustomerId)
            return "Select a sales order that belongs to the payment's customer.";
        if (!IsAllocatable(order))
            return "Only approved or delivered sales orders can receive payments.";

        var unallocated = payment.Amount - Allocated(payment);
        if (amount > unallocated)
            return $"The amount exceeds the unallocated payment balance ({unallocated:0.00##}).";

        var paid = (await GetPaidByOrderAsync([orderId], cancellationToken)).GetValueOrDefault(orderId);
        var outstanding = order.Total - paid;
        if (amount > outstanding)
            return $"The amount exceeds the order's outstanding balance ({outstanding:0.00##}).";

        var existing = payment.Allocations.FirstOrDefault(row => row.SalesOrderHeaderId == orderId);
        if (existing is null)
        {
            dbContext.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
            {
                CustomerPaymentId = paymentId,
                SalesOrderHeaderId = orderId,
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

    public static string? ValidateAmountChange(CustomerPayment payment, decimal newAmount) =>
        newAmount < Allocated(payment)
            ? $"The amount cannot be lower than what is already allocated ({Allocated(payment):0.00##})."
            : null;

    private static bool IsAllocatable(SalesOrderHeader order) =>
        AllocatableStatuses.Contains(order.Status.Name.Trim().ToUpperInvariant());
}
