using System.Security.Claims;
using ComdisAI.Authorization;
using ComdisAI.Controllers;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Services;
using ComdisAI.ViewModels.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class HomeDashboardTests
{
    [Fact]
    public async Task Index_returns_a_home_dashboard_view_model_from_the_database()
    {
        using var database = await CreateDatabaseAsync();
        var result = await CreateController(database.Context).Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<HomeDashboardViewModel>(view.Model);
    }

    [Fact]
    public async Task Dashboard_exposes_four_complete_kpi_contracts()
    {
        using var database = await CreateDatabaseAsync();
        var model = await GetDashboardAsync(database.Context);
        var kpis = new[]
        {
            model.MonthlySales,
            model.SalesOrdersToFulfill,
            model.PurchasesInTransit,
            model.DeliveriesToday
        };

        Assert.Equal(
            ["Ventas del mes", "Pedidos por surtir", "Compras en tránsito", "Entregas hoy"],
            kpis.Select(kpi => kpi.Label));
        Assert.All(kpis, kpi =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kpi.Value));
            Assert.False(string.IsNullOrWhiteSpace(kpi.Detail));
            Assert.False(string.IsNullOrWhiteSpace(kpi.SupportingText));
            Assert.InRange(kpi.ProgressPercent, 0m, 100m);
            Assert.StartsWith("bi-", kpi.IconClass);
        });
    }

    [Fact]
    public async Task Dashboard_aggregates_real_order_product_and_catalog_data()
    {
        using var database = await CreateDatabaseAsync();
        await SeedOperationalDataAsync(database.Context);

        var model = await GetDashboardAsync(database.Context);

        Assert.Equal("1", model.SalesOrdersToFulfill.Value);
        Assert.Equal("1", model.PurchasesInTransit.Value);
        Assert.Equal("2", model.DeliveriesToday.Value);
        Assert.Contains("150", model.MonthlySales.Value);
        Assert.Collection(model.ActiveSalesOrders,
            row => Assert.Equal("2026-00001", row.OrderNumber));
        Assert.Collection(model.PurchaseOrders,
            row => Assert.Equal("2026-01001", row.OrderNumber));
        Assert.Collection(model.TopProducts,
            row =>
            {
                Assert.Equal("A0001", row.Code);
                Assert.Equal(3m, row.UnitsSold);
                Assert.Equal(100m, row.RotationPercent);
            });
        Assert.Collection(model.ProductSearchResults,
            row => Assert.Equal("A0001", row.Code));
        Assert.Collection(model.CustomerSearchResults,
            row => Assert.Equal("Cliente real", row.Name));
        Assert.Collection(model.OperationalAlerts,
            alert => Assert.Contains("1 pedidos pendientes", alert.Title),
            alert => Assert.Contains("1 arribos", alert.Title),
            alert => Assert.Contains("1 productos activos", alert.Title));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Dashboard_preserves_create_permission_flags(
        bool canCreateSalesOrder,
        bool canCreatePurchaseOrder)
    {
        using var database = await CreateDatabaseAsync();
        var permissions = new List<string>();
        if (canCreateSalesOrder)
            permissions.Add(AuthorizationActionCodes.CreateSalesOrder);
        if (canCreatePurchaseOrder)
            permissions.Add(AuthorizationActionCodes.CreatePurchaseOrder);

        var model = await GetDashboardAsync(database.Context, permissions);

        Assert.Equal(canCreateSalesOrder, model.CanCreateSalesOrder);
        Assert.Equal(canCreatePurchaseOrder, model.CanCreatePurchaseOrder);
    }

    private static async Task<HomeDashboardViewModel> GetDashboardAsync(
        AppDbContext context,
        IEnumerable<string>? actionCodes = null)
    {
        var result = await CreateController(context, actionCodes ?? []).Index(CancellationToken.None);
        return Assert.IsType<HomeDashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
    }

    private static HomeController CreateController(
        AppDbContext context,
        IEnumerable<string>? actionCodes = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "Mariana López")],
            "test"));
        var httpContext = new DefaultHttpContext { User = principal };

        return new HomeController(
            new StubActionPermissionService(actionCodes ?? []),
            context)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static async Task<TestDatabase> CreateDatabaseAsync()
    {
        Batteries.Init();
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options,
            new HttpContextAccessor());
        await context.Database.EnsureCreatedAsync();
        return new TestDatabase(connection, context);
    }

    private static async Task SeedOperationalDataAsync(AppDbContext context)
    {
        var today = DateTime.Today;
        var approvedSales = new Status { Name = "Approved", Entity = "SalesOrder" };
        var deliveredSales = new Status { Name = "Delivered", Entity = "SalesOrder" };
        var approvedPurchase = new Status { Name = "Approved", Entity = "PurchaseOrder" };
        var tax = new Tax { Name = "IVA 16", Value = 16m };
        var uom = new Uom { Name = "Pieza" };
        var category = new ProductCategory { Name = "General", Prefix = "A" };
        var bank = new Bank { Name = "Banco" };
        context.AddRange(approvedSales, deliveredSales, approvedPurchase, tax, uom, category, bank);
        await context.SaveChangesAsync();

        var customer = new Customer
        {
            Name = "Cliente real",
            Address = "Av. Principal 123",
            RFC = "CRE010101ABC"
        };
        var supplier = new Supplier
        {
            Name = "Proveedor real",
            Address = "Av. Industrial 456",
            RFC = "PRE010101ABC",
            BankId = bank.Id,
            BankAccount = "1234567890"
        };
        var product = new Product
        {
            Name = "Producto real",
            Code = "A0001",
            UomId = uom.Id,
            ProductCategoryId = category.Id
        };
        context.AddRange(customer, supplier, product);
        await context.SaveChangesAsync();

        var pendingOrder = new SalesOrderHeader
        {
            OrderNumber = "2026-00001",
            CustomerId = customer.Id,
            DeliveryAddress = customer.Address,
            DeliveryDate = today,
            StatusId = approvedSales.Id,
            IsDelivered = false,
            TaxId = tax.Id,
            Total = 100m,
            Details =
            [
                new SalesOrderDetail { ProductId = product.Id, Qty = 2m, Price = 50m, SubTotal = 100m }
            ]
        };
        var deliveredOrder = new SalesOrderHeader
        {
            OrderNumber = "2026-00002",
            CustomerId = customer.Id,
            DeliveryAddress = customer.Address,
            DeliveryDate = today,
            StatusId = deliveredSales.Id,
            IsDelivered = true,
            TaxId = tax.Id,
            Total = 50m,
            Details =
            [
                new SalesOrderDetail { ProductId = product.Id, Qty = 3m, Price = 16.6667m, SubTotal = 50m }
            ]
        };
        var previousMonthOrder = new SalesOrderHeader
        {
            OrderNumber = "2026-00003",
            CustomerId = customer.Id,
            DeliveryAddress = customer.Address,
            DeliveryDate = new DateTime(today.Year, today.Month, 1).AddMonths(-1).AddDays(1),
            StatusId = deliveredSales.Id,
            IsDelivered = true,
            TaxId = tax.Id,
            Total = 40m
        };
        var purchaseOrder = new PurchaseOrderHeader
        {
            OrderNumber = "2026-01001",
            SupplierId = supplier.Id,
            SupplyAddress = supplier.Address,
            SupplyDate = today.AddDays(2),
            StatusId = approvedPurchase.Id,
            TaxId = tax.Id,
            Total = 200m
        };
        context.AddRange(pendingOrder, deliveredOrder, previousMonthOrder, purchaseOrder);
        await context.SaveChangesAsync();
    }

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

    private sealed class StubActionPermissionService(IEnumerable<string> actionCodes) : IActionPermissionService
    {
        private readonly HashSet<string> _actionCodes = actionCodes.ToHashSet(StringComparer.Ordinal);

        public Task<IReadOnlyList<RoleSelectionOptionViewModel>> GetActiveRolesAsync(
            int userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleSelectionOptionViewModel>>([]);

        public Task<IReadOnlyList<string>> GetActiveActionCodesAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_actionCodes.ToList());

        public Task<bool> HasActionAsync(
            ClaimsPrincipal principal,
            string actionCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_actionCodes.Contains(actionCode));
    }
}
