using System.Reflection;
using ComdisAI.Authorization;
using ComdisAI.Controllers;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.ViewModels.PurchaseReports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class PurchaseReportTests
{
    [Fact]
    public async Task Report_defaults_to_today_and_lists_only_purchase_order_statuses()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedLookupsAsync(database.Context);
        AddOrder(database.Context, setup, setup.PurchaseStatus, DateTime.Today, "2026-00001", 15m);
        AddOrder(database.Context, setup, setup.PurchaseStatus, DateTime.Today.AddDays(-1), "2026-00002", 20m);
        await database.Context.SaveChangesAsync();

        var result = await new PurchaseReportsController(database.Context).Index(
            null,
            null,
            null,
            CancellationToken.None);

        var model = Assert.IsType<PurchaseReportViewModel>(Assert.IsType<ViewResult>(result).Model);
        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Equal(today, model.StartDate);
        Assert.Equal(today, model.EndDate);
        Assert.Collection(model.Orders, order => Assert.Equal("2026-00001", order.OrderNumber));
        Assert.Collection(model.StatusOptions, status => Assert.Equal("Approved", status.Text));
        Assert.Equal(15m, model.PeriodTotal);
    }

    [Fact]
    public async Task Report_includes_both_date_boundaries_and_orders_rows_by_supply_date_and_number()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedLookupsAsync(database.Context);
        AddOrder(database.Context, setup, setup.PurchaseStatus, new DateTime(2026, 11, 10, 0, 0, 0), "2026-00002", 10m);
        AddOrder(database.Context, setup, setup.PurchaseStatus, new DateTime(2026, 11, 10, 0, 0, 0), "2026-00001", 15m);
        AddOrder(database.Context, setup, setup.PurchaseStatus, new DateTime(2026, 11, 11, 0, 0, 0), "2026-00003", 20m);
        AddOrder(database.Context, setup, setup.PurchaseStatus, new DateTime(2026, 11, 11, 23, 59, 59), "2026-00004", 25m);
        AddOrder(database.Context, setup, setup.PurchaseStatus, new DateTime(2026, 11, 12, 0, 0, 0), "2026-00005", 30m);
        await database.Context.SaveChangesAsync();

        var result = await new PurchaseReportsController(database.Context).Index(
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 11),
            null,
            CancellationToken.None);

        var model = Assert.IsType<PurchaseReportViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(
            ["2026-00001", "2026-00002", "2026-00003", "2026-00004"],
            model.Orders.Select(order => order.OrderNumber));
        Assert.Equal(70m, model.PeriodTotal);
        var summary = Assert.Single(model.StatusSummary);
        Assert.Equal("Approved", summary.StatusName);
        Assert.Equal(4, summary.OrderCount);
        Assert.Equal(70m, summary.Total);
    }

    [Fact]
    public async Task Report_filters_multiple_purchase_statuses_and_rejects_sales_statuses()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedLookupsAsync(database.Context);
        var draft = new Status { Name = "Draft", Entity = "PurchaseOrder" };
        database.Context.Statuses.Add(draft);
        await database.Context.SaveChangesAsync();
        AddOrder(database.Context, setup, setup.PurchaseStatus, new DateTime(2026, 11, 10), "2026-00001", 10m);
        AddOrder(database.Context, setup, draft, new DateTime(2026, 11, 10), "2026-00002", 20m);
        await database.Context.SaveChangesAsync();
        var controller = new PurchaseReportsController(database.Context);

        var filteredResult = await controller.Index(
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 10),
            [setup.PurchaseStatus.Id, draft.Id],
            CancellationToken.None);
        var filteredModel = Assert.IsType<PurchaseReportViewModel>(
            Assert.IsType<ViewResult>(filteredResult).Model);
        Assert.Equal(2, filteredModel.Orders.Count);
        Assert.Equal(30m, filteredModel.PeriodTotal);
        Assert.Equal(2, filteredModel.StatusSummary.Count);

        var invalidResult = await controller.Index(
            new DateOnly(2026, 11, 10),
            new DateOnly(2026, 11, 10),
            [setup.SalesStatus.Id],
            CancellationToken.None);
        var invalidModel = Assert.IsType<PurchaseReportViewModel>(Assert.IsType<ViewResult>(invalidResult).Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(invalidModel.Orders);
        Assert.Contains(controller.ModelState[nameof(PurchaseReportViewModel.StatusIds)]!.Errors,
            error => error.ErrorMessage == "Select valid purchase order statuses.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Report_rejects_incomplete_or_reversed_ranges(bool incompleteRange)
    {
        using var database = await CreateDatabaseAsync();
        await SeedLookupsAsync(database.Context);
        var controller = new PurchaseReportsController(database.Context);
        var startDate = incompleteRange ? (DateOnly?)null : new DateOnly(2026, 11, 11);
        var endDate = new DateOnly(2026, 11, 10);

        var result = await controller.Index(startDate, endDate, null, CancellationToken.None);

        var model = Assert.IsType<PurchaseReportViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(model.Orders);
    }

    [Fact]
    public void Index_requires_the_purchase_report_view_action()
    {
        var method = typeof(PurchaseReportsController).GetMethod(
            nameof(PurchaseReportsController.Index),
            [typeof(DateOnly?), typeof(DateOnly?), typeof(int[]), typeof(CancellationToken)]);
        var attribute = Assert.IsType<RequireActionAttribute>(
            method?.GetCustomAttribute<RequireActionAttribute>());

        var actionCode = Assert.Single(attribute.Arguments ?? []);
        Assert.Equal(AuthorizationActionCodes.ViewPurchaseReport, actionCode);
    }

    private static void AddOrder(
        AppDbContext context,
        ReportSetup setup,
        Status status,
        DateTime supplyDate,
        string orderNumber,
        decimal total)
    {
        context.PurchaseOrderHeaders.Add(new PurchaseOrderHeader
        {
            OrderNumber = orderNumber,
            Supplier = setup.Supplier,
            SupplyAddress = setup.Supplier.Address,
            SupplyDate = supplyDate,
            Status = status,
            Tax = setup.Tax,
            Total = total
        });
    }

    private static async Task<ReportSetup> SeedLookupsAsync(AppDbContext context)
    {
        var supplier = new Supplier
        {
            Name = "Sample Supplier",
            Address = "10 Main Street",
            RFC = "RFC123",
            Bank = new Bank { Name = "Sample Bank" },
            BankAccount = "123456"
        };
        var tax = new Tax { Name = "VAT", Value = 0.16m };
        var purchaseStatus = new Status { Name = "Approved", Entity = "PurchaseOrder" };
        var salesStatus = new Status { Name = "Delivered", Entity = "SalesOrder" };
        context.AddRange(supplier, tax, purchaseStatus, salesStatus);
        await context.SaveChangesAsync();
        return new ReportSetup(supplier, tax, purchaseStatus, salesStatus);
    }

    private static async Task<TestDatabase> CreateDatabaseAsync()
    {
        Batteries.Init();
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());
        await context.Database.EnsureCreatedAsync();
        return new TestDatabase(connection, context);
    }

    private sealed record ReportSetup(
        Supplier Supplier,
        Tax Tax,
        Status PurchaseStatus,
        Status SalesStatus);

    private sealed class TestDatabase(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        AppDbContext context) : IDisposable
    {
        public AppDbContext Context { get; } = context;

        public void Dispose()
        {
            Context.Dispose();
            connection.Dispose();
        }
    }
}
