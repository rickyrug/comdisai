using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        var order1 = NewOrder(customer, approved, tax, "2026-00001", 100m);
        var order2 = NewOrder(customer, draft, tax, "2026-00002", 100m);
        var order3 = NewOrder(other, approved, tax, "2026-00003", 100m);
        var payment = new CustomerPayment
        {
            Customer = customer, Amount = 150m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        };
        context.AddRange(order1, order2, order3, payment);
        await context.SaveChangesAsync();
        var service = new CustomerPaymentService(context);

        Assert.Null(await service.AllocateAsync(payment.Id, order1.Id, 60m));
        Assert.Null(await service.AllocateAsync(payment.Id, order1.Id, 40m));
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
}
