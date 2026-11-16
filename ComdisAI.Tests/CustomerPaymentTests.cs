using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class CustomerPaymentTests
{
    private static async Task<(Microsoft.Data.Sqlite.SqliteConnection, AppDbContext)> CreateAsync()
    {
        Batteries.Init();
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());
        await context.Database.EnsureCreatedAsync();
        return (connection, context);
    }

    private static SalesOrderHeader NewOrder(
        Customer customer, Status status, Tax tax, string number, decimal total) => new()
    {
        OrderNumber = number,
        Customer = customer,
        DeliveryAddress = "Street",
        DeliveryDate = DateTime.Today,
        Status = status,
        Tax = tax,
        Total = total
    };

    [Fact]
    public async Task Allocation_enforces_balances_customer_and_status_rules()
    {
        var (connection, context) = await CreateAsync();
        await using var _ = context;
        using var __ = connection;
        var customer = new Customer { Name = "A", Address = "x", RFC = "R1" };
        var other = new Customer { Name = "B", Address = "x", RFC = "R2" };
        var tax = new Tax { Name = "VAT", Value = 0.16m };
        var approved = new Status { Name = "Approved", Entity = "SalesOrder" };
        var draft = new Status { Name = "Draft", Entity = "SalesOrder" };
        var paidStatus = new Status { Name = "Paid", Entity = "SalesOrder" };
        var order1 = NewOrder(customer, approved, tax, "2026-00001", 100m);
        var order2 = NewOrder(customer, draft, tax, "2026-00002", 100m);
        var order3 = NewOrder(other, approved, tax, "2026-00003", 100m);
        var payment = new CustomerPayment
        {
            Customer = customer, Amount = 150m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        };
        context.AddRange(order1, order2, order3, payment, paidStatus);
        await context.SaveChangesAsync();
        var service = new CustomerPaymentService(context);

        Assert.Null(await service.AllocateAsync(payment.Id, order1.Id, 60m));
        Assert.Equal("Approved", order1.Status.Name);
        Assert.Null(await service.AllocateAsync(payment.Id, order1.Id, 40m));
        Assert.Equal("Paid", order1.Status.Name);
        Assert.NotNull(await service.AllocateAsync(payment.Id, order1.Id, 1m));
        Assert.NotNull(await service.AllocateAsync(payment.Id, order2.Id, 10m));
        Assert.NotNull(await service.AllocateAsync(payment.Id, order3.Id, 10m));
        Assert.NotNull(await service.AllocateAsync(payment.Id, order1.Id, 0m));

        var allocations = await context.CustomerPaymentAllocations.ToListAsync();
        Assert.Equal(100m, Assert.Single(allocations).Amount);
        Assert.Empty(await service.GetOpenOrdersAsync(customer.Id));
    }

    [Fact]
    public async Task Allocation_cannot_exceed_unallocated_payment_amount()
    {
        var (connection, context) = await CreateAsync();
        await using var _ = context;
        using var __ = connection;
        var customer = new Customer { Name = "A", Address = "x", RFC = "R1" };
        var tax = new Tax { Name = "VAT", Value = 0.16m };
        var status = new Status { Name = "Delivered", Entity = "SalesOrder" };
        var order = NewOrder(customer, status, tax, "2026-00001", 500m);
        var payment = new CustomerPayment
        {
            Customer = customer, Amount = 50m, Method = PaymentMethod.Transfer, PaymentDate = DateTime.Today
        };
        context.AddRange(order, payment);
        await context.SaveChangesAsync();
        var service = new CustomerPaymentService(context);

        Assert.NotNull(await service.AllocateAsync(payment.Id, order.Id, 50.01m));
        Assert.Null(await service.AllocateAsync(payment.Id, order.Id, 50m));
        Assert.Equal(50m, (await service.GetPaidByOrderAsync([order.Id]))[order.Id]);
        Assert.NotNull(CustomerPaymentService.ValidateAmountChange(
            new CustomerPayment { Allocations = [new CustomerPaymentAllocation { Amount = 50m }] }, 40m));
    }

    [Theory]
    [InlineData("Approved", false)]
    [InlineData("Delivered", true)]
    public async Task Removing_or_deleting_payment_restores_fulfillment_status(string initialStatus, bool delivered)
    {
        var (connection, context) = await CreateAsync();
        await using var _ = context;
        using var __ = connection;
        var customer = new Customer { Name = "A", Address = "x", RFC = "R1" };
        var tax = new Tax { Name = "VAT", Value = 0m };
        var status = new Status { Name = initialStatus, Entity = "SalesOrder" };
        var paidStatus = new Status { Name = "Paid", Entity = "SalesOrder" };
        var order = NewOrder(customer, status, tax, "2026-00001", 100m);
        var firstPayment = new CustomerPayment { Customer = customer, Amount = 60m, Method = PaymentMethod.Cash };
        var secondPayment = new CustomerPayment { Customer = customer, Amount = 40m, Method = PaymentMethod.Cash };
        context.AddRange(order, firstPayment, secondPayment, paidStatus);
        await context.SaveChangesAsync();
        var service = new CustomerPaymentService(context);

        Assert.Null(await service.AllocateAsync(firstPayment.Id, order.Id, 60m));
        Assert.Null(await service.AllocateAsync(secondPayment.Id, order.Id, 40m));
        Assert.Equal("Paid", order.Status.Name);
        Assert.Equal(delivered, order.IsDelivered);

        var allocation = await context.CustomerPaymentAllocations.SingleAsync(row => row.CustomerPaymentId == secondPayment.Id);
        Assert.Null(await service.RemoveAllocationAsync(allocation));
        Assert.Equal(initialStatus, order.Status.Name);
        Assert.Equal(40m, Assert.Single(await service.GetOpenOrdersAsync(customer.Id)).Balance);
        Assert.Null(await service.AllocateAsync(secondPayment.Id, order.Id, 40m));
        Assert.Null(await service.DeletePaymentAsync(firstPayment));
        context.ChangeTracker.Clear();

        var savedOrder = await context.SalesOrderHeaders.Include(row => row.Status).SingleAsync();
        Assert.Equal(initialStatus, savedOrder.Status.Name);
        Assert.Equal(delivered, savedOrder.IsDelivered);
        Assert.Equal(40m, (await service.GetPaidByOrderAsync([order.Id]))[order.Id]);
        Assert.Equal(60m, Assert.Single(await service.GetOpenOrdersAsync(customer.Id)).Balance);
    }

    [Fact]
    public async Task Missing_paid_status_rejects_final_allocation_without_saving_it()
    {
        var (connection, context) = await CreateAsync();
        await using var _ = context;
        using var __ = connection;
        var customer = new Customer { Name = "A", Address = "x", RFC = "R1" };
        var order = NewOrder(customer, new Status { Name = "Approved", Entity = "SalesOrder" },
            new Tax { Name = "VAT", Value = 0m }, "2026-00001", 100m);
        var payment = new CustomerPayment { Customer = customer, Amount = 100m, Method = PaymentMethod.Cash };
        context.AddRange(order, payment);
        await context.SaveChangesAsync();

        Assert.NotNull(await new CustomerPaymentService(context).AllocateAsync(payment.Id, order.Id, 100m));
        Assert.Empty(await context.CustomerPaymentAllocations.ToListAsync());
        Assert.Equal("Approved", order.Status.Name);
    }

    [Fact]
    public async Task Backfill_is_idempotent_and_ignores_deleted_payments_and_unallocated_orders()
    {
        var (connection, context) = await CreateAsync();
        await using var _ = context;
        using var __ = connection;
        var customer = new Customer { Name = "A", Address = "x", RFC = "R1" };
        var tax = new Tax { Name = "VAT", Value = 0m };
        var approved = new Status { Name = "Approved", Entity = "SalesOrder" };
        var delivered = new Status { Name = "Delivered", Entity = "SalesOrder" };
        var inactivePaid = new Status { Name = "Paid", Entity = "SalesOrder", IsDeleted = true };
        var first = NewOrder(customer, approved, tax, "2026-00001", 0.3m);
        var second = NewOrder(customer, delivered, tax, "2026-00002", 20m);
        var zero = NewOrder(customer, approved, tax, "2026-00003", 0m);
        var deleted = NewOrder(customer, approved, tax, "2026-00004", 10m);
        var payment = new CustomerPayment { Customer = customer, Amount = 20.3m, Method = PaymentMethod.Cash };
        var fractionalPayment = new CustomerPayment { Customer = customer, Amount = 0.2m, Method = PaymentMethod.Cash };
        var deletedPayment = new CustomerPayment { Customer = customer, Amount = 10m, Method = PaymentMethod.Cash, IsDeleted = true };
        context.AddRange(first, second, zero, deleted, inactivePaid, payment, fractionalPayment, deletedPayment);
        context.AddRange(
            new CustomerPaymentAllocation { CustomerPayment = payment, SalesOrderHeader = first, Amount = 0.1m },
            new CustomerPaymentAllocation { CustomerPayment = fractionalPayment, SalesOrderHeader = first, Amount = 0.2m },
            new CustomerPaymentAllocation { CustomerPayment = payment, SalesOrderHeader = second, Amount = 20m },
            new CustomerPaymentAllocation { CustomerPayment = deletedPayment, SalesOrderHeader = deleted, Amount = 10m });
        await context.SaveChangesAsync();
        var service = new CustomerPaymentService(context);

        await service.BackfillPaidOrdersAsync(NullLogger.Instance);
        await service.BackfillPaidOrdersAsync(NullLogger.Instance);
        context.ChangeTracker.Clear();
        var orders = await context.SalesOrderHeaders.Include(row => row.Status).OrderBy(row => row.OrderNumber).ToListAsync();
        Assert.Equal(new[] { "Paid", "Paid", "Approved", "Approved" }, orders.Select(row => row.Status.Name));
        Assert.False(orders[0].IsDelivered);
        Assert.True(orders[1].IsDelivered);
        Assert.Single(await context.Statuses.IgnoreQueryFilters().Where(row => row.Name == "Paid").ToListAsync());
    }

    [Fact]
    public async Task Removing_an_allocation_from_a_deleted_payment_does_not_change_the_order()
    {
        var (connection, context) = await CreateAsync();
        await using var _ = context;
        using var __ = connection;
        var customer = new Customer { Name = "A", Address = "x", RFC = "R1" };
        var order = NewOrder(customer, new Status { Name = "Approved", Entity = "SalesOrder" },
            new Tax { Name = "VAT", Value = 0m }, "2026-00001", 100m);
        var payment = new CustomerPayment { Customer = customer, Amount = 100m, Method = PaymentMethod.Cash, IsDeleted = true };
        var allocation = new CustomerPaymentAllocation { CustomerPayment = payment, SalesOrderHeader = order, Amount = 100m };
        context.Add(allocation);
        await context.SaveChangesAsync();

        Assert.NotNull(await new CustomerPaymentService(context).RemoveAllocationAsync(allocation));
        Assert.Equal("Approved", order.Status.Name);
        Assert.Single(await context.CustomerPaymentAllocations.ToListAsync());
    }
}
