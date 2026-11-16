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
                .Where(allocation => orderIds.Contains(allocation.SalesOrderHeaderId) && !allocation.CustomerPayment.IsDeleted)
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
                .Where(allocation => ids.Contains(allocation.SalesOrderHeaderId) && !allocation.CustomerPayment.IsDeleted)
                .Select(allocation => new { allocation.SalesOrderHeaderId, allocation.Amount })
                .ToListAsync(cancellationToken))
            .GroupBy(allocation => allocation.SalesOrderHeaderId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
    }

    public async Task BackfillPaidOrdersAsync(ILogger logger, CancellationToken cancellationToken = default)
    {
        var unknownDelivery = await dbContext.SalesOrderHeaders.IgnoreQueryFilters()
            .Where(order => order.IsDelivered == null)
            .Select(order => order.OrderNumber).ToListAsync(cancellationToken);
        foreach (var orderNumber in unknownDelivery)
            logger.LogWarning("Sales order {OrderNumber} has unknown delivery state and requires manual review.", orderNumber);

        var paidStatus = await dbContext.Statuses.IgnoreQueryFilters().SingleOrDefaultAsync(status =>
            EF.Property<string>(status, "NormalizedEntity") == "SALESORDER" &&
            EF.Property<string>(status, "NormalizedName") == "PAID", cancellationToken);
        if (paidStatus is null)
        {
            paidStatus = new Status { Name = "Paid", Entity = "SalesOrder" };
            dbContext.Statuses.Add(paidStatus);
        }
        else if (paidStatus.IsDeleted)
        {
            paidStatus.IsDeleted = false;
            paidStatus.DeletedAtUtc = null;
            paidStatus.DeletedBy = null;
        }

        var orders = await dbContext.SalesOrderHeaders.Include(order => order.Status)
            .Where(order => EF.Property<string>(order.Status, "NormalizedEntity") == "SALESORDER" &&
                (EF.Property<string>(order.Status, "NormalizedName") == "APPROVED" ||
                 EF.Property<string>(order.Status, "NormalizedName") == "DELIVERED"))
            .ToListAsync(cancellationToken);
        var paid = await GetPaidByOrderAsync(orders.Select(order => order.Id), cancellationToken);
        foreach (var order in orders)
        {
            var amount = paid.GetValueOrDefault(order.Id);
            if (amount > 0m && amount == order.Total)
            {
                order.IsDelivered = order.IsDelivered == true || order.Status.Name.Trim().Equals("Delivered", StringComparison.OrdinalIgnoreCase);
                order.Status = paidStatus;
            }
            else if (amount > order.Total)
                logger.LogWarning("Sales order {OrderNumber} has allocations exceeding its total and needs review.", order.OrderNumber);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
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

        var statusError = await ReconcileStatusAsync(order, paid + amount, cancellationToken);
        if (statusError is not null)
            return statusError;

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
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ReconcileStatusAsync(
        SalesOrderHeader order, decimal paid, CancellationToken cancellationToken = default)
    {
        var current = order.Status.Name.Trim().ToUpperInvariant();
        if (current is not ("APPROVED" or "DELIVERED" or "PAID"))
            return null;

        if (current == "PAID" && order.IsDelivered is null)
            return "The order's delivery state is unknown and requires manual review.";
        var delivered = order.IsDelivered == true || current == "DELIVERED";
        var target = paid > 0m && paid == order.Total ? "PAID" : delivered ? "DELIVERED" : "APPROVED";
        if (target != current)
        {
            var status = await dbContext.Statuses.SingleOrDefaultAsync(row =>
                EF.Property<string>(row, "NormalizedEntity") == "SALESORDER" &&
                EF.Property<string>(row, "NormalizedName") == target, cancellationToken);
            if (status is null)
                return $"SalesOrder status '{target}' is not configured. Add it in Statuses first.";
            order.Status = status;
        }

        order.IsDelivered = delivered;
        return null;
    }

    public async Task<string?> RemoveAllocationAsync(
        CustomerPaymentAllocation allocation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await dbContext.CustomerPaymentAllocations.AnyAsync(row =>
            row.Id == allocation.Id && !row.CustomerPayment.IsDeleted, cancellationToken))
            return "The allocation was not found on an active payment.";
        var order = await dbContext.SalesOrderHeaders.Include(row => row.Status)
            .SingleAsync(row => row.Id == allocation.SalesOrderHeaderId, cancellationToken);
        var paid = (await GetPaidByOrderAsync([order.Id], cancellationToken)).GetValueOrDefault(order.Id);
        var error = await ReconcileStatusAsync(order, paid - allocation.Amount, cancellationToken);
        if (error is not null)
            return error;
        dbContext.CustomerPaymentAllocations.Remove(allocation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public async Task<string?> DeletePaymentAsync(CustomerPayment payment, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var allocations = await dbContext.CustomerPaymentAllocations
            .Where(row => row.CustomerPaymentId == payment.Id)
            .ToListAsync(cancellationToken);
        var ids = allocations.Select(row => row.SalesOrderHeaderId).ToArray();
        var paid = await GetPaidByOrderAsync(ids, cancellationToken);
        var orders = await dbContext.SalesOrderHeaders.Include(row => row.Status)
            .Where(row => ids.Contains(row.Id)).ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            var amount = allocations.Where(row => row.SalesOrderHeaderId == order.Id).Sum(row => row.Amount);
            var error = await ReconcileStatusAsync(order, paid.GetValueOrDefault(order.Id) - amount, cancellationToken);
            if (error is not null)
                return error;
        }

        payment.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    public static string? ValidateAmountChange(CustomerPayment payment, decimal newAmount) =>
        newAmount < Allocated(payment)
            ? $"The amount cannot be lower than what is already allocated ({Allocated(payment):0.00##})."
            : null;

    private static bool IsAllocatable(SalesOrderHeader order) =>
        AllocatableStatuses.Contains(order.Status.Name.Trim().ToUpperInvariant());
}
