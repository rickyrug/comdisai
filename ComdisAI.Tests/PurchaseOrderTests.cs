using System.Security.Claims;
using ComdisAI.Authorization;
using ComdisAI.Controllers;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.Services;
using ComdisAI.ViewModels.Authentication;
using ComdisAI.ViewModels.PurchaseOrders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf.IO;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class PurchaseOrderTests
{
    [Fact]
    public void Pricing_applies_line_discount_then_sequential_header_discounts_and_tax()
    {
        var tax = NewTax();
        var order = new PurchaseOrderHeader
        {
            Tax = tax,
            DiscountPercent1 = 10m,
            DiscountPercent2 = 5m,
            DiscountPercent3 = 20m,
            Details =
            [
                new PurchaseOrderDetail { Price = 100m, Qty = 2m, DiscountPercent = 10m },
                new PurchaseOrderDetail { Price = 50m, Qty = 1m }
            ]
        };

        PurchaseOrderPricing.Recalculate(order);

        Assert.Equal(180m, order.Details.First().SubTotal);
        Assert.Equal(230m, order.Subtotal);
        Assert.Equal(25.1712m, order.TaxAmount);
        Assert.Equal(182.4912m, order.Total);
    }

    [Fact]
    public void Pricing_rounds_each_amount_to_four_places_away_from_zero()
    {
        var order = new PurchaseOrderHeader
        {
            Tax = NewTax(0m),
            Details = [new PurchaseOrderDetail { Price = 0.0001m, Qty = 0.5m }]
        };

        PurchaseOrderPricing.Recalculate(order);

        Assert.Equal(0.0001m, order.Details.Single().SubTotal);
        Assert.Equal(0.0001m, order.Subtotal);
    }

    [Fact]
    public void Pricing_rejects_calculated_amounts_outside_decimal_18_4()
    {
        var order = new PurchaseOrderHeader
        {
            Tax = NewTax(0m),
            Details =
            [
                new PurchaseOrderDetail
                {
                    Price = 99_999_999_999_999.9999m,
                    Qty = 99_999_999_999_999.9999m
                }
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseOrderPricing.Recalculate(order));
    }

    [Fact]
    public void Order_number_sequence_is_year_scoped_and_padded_to_five_digits()
    {
        Assert.Equal(
            "2026-00012",
            PurchaseOrderNumberGenerator.NextNumber(
                2026,
                ["2025-00099", "2026-00002", "2026-00011"]));
        Assert.Equal("2027-00001", PurchaseOrderNumberGenerator.NextNumber(2027, ["2026-99999"]));
    }

    [Fact]
    public void Order_number_sequence_rejects_malformed_values_and_exhaustion()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PurchaseOrderNumberGenerator.NextNumber(2026, ["2026-12"]));
        Assert.Throws<InvalidOperationException>(() =>
            PurchaseOrderNumberGenerator.NextNumber(2026, ["2026-99999"]));
    }

    [Fact]
    public async Task Create_get_exposes_supplier_addresses_for_default_supply_address()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var controller = CreateController(database.Context);

        var result = await controller.Create(CancellationToken.None);

        var model = Assert.IsType<PurchaseOrderHeaderInputViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(setup.Supplier.Address, model.SupplierAddresses[setup.Supplier.Id.ToString()]);
    }

    [Fact]
    public async Task Create_assigns_the_next_number_draft_status_and_audit_values()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var controller = CreateController(database.Context);

        var result = await controller.Create(
            new PurchaseOrderHeaderInputViewModel
            {
                SupplierId = setup.Supplier.Id,
                SupplyAddress = "  10 Main Street  ",
                SupplyDate = DateTime.Today.AddDays(1),
                TaxId = setup.Tax.Id
            },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        var order = await database.Context.PurchaseOrderHeaders
            .Include(row => row.Status)
            .SingleAsync();
        Assert.Matches(@"^\d{4}-00001$", order.OrderNumber);
        Assert.Equal("Draft", order.Status.Name);
        Assert.Equal("10 Main Street", order.SupplyAddress);
        Assert.Equal("system", order.CreatedBy);
        Assert.NotEqual(default, order.CreatedAtUtc);
    }

    [Fact]
    public async Task Creator_can_enter_initial_details_on_an_empty_draft()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var order = await AddOrderAsync(database.Context, setup, setup.Statuses["Draft"]);
        database.Context.PurchaseOrderDetails.RemoveRange(order.Details);
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context, AuthorizationActionCodes.CreatePurchaseOrder)
            .Edit(order.Id, CancellationToken.None);

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Editing_approved_order_resets_to_draft_and_hard_deletes_removed_lines()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var approved = setup.Statuses["Approved"];
        var order = await AddOrderAsync(database.Context, setup, approved);
        var existingLineId = order.Details.Single().Id;

        var result = await CreateController(database.Context, AuthorizationActionCodes.EditPurchaseOrder).Edit(
            order.Id,
            new PurchaseOrderEditViewModel
            {
                SupplierId = setup.Supplier.Id,
                SupplyAddress = order.SupplyAddress,
                SupplyDate = order.SupplyDate,
                TaxId = setup.Tax.Id,
                Details = []
            },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Draft", (await database.Context.PurchaseOrderHeaders
            .Include(row => row.Status)
            .SingleAsync()).Status.Name);
        Assert.False(await database.Context.PurchaseOrderDetails.AnyAsync(row => row.Id == existingLineId));
    }

    [Fact]
    public async Task Supplied_order_requires_privileged_edit_permission_and_remains_supplied()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var order = await AddOrderAsync(database.Context, setup, setup.Statuses["Supplied"]);
        var input = new PurchaseOrderEditViewModel
        {
            SupplierId = setup.Supplier.Id,
            SupplyAddress = "Updated supply address",
            SupplyDate = order.SupplyDate,
            TaxId = setup.Tax.Id,
            Details = []
        };

        var regularController = CreateController(database.Context, AuthorizationActionCodes.EditPurchaseOrder);
        var denied = await regularController.Edit(order.Id, input, CancellationToken.None);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ViewResult>(denied).StatusCode);

        var privilegedController = CreateController(
            database.Context,
            AuthorizationActionCodes.EditSuppliedPurchaseOrder);
        var allowed = await privilegedController.Edit(order.Id, input, CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(allowed);
        Assert.Equal("Supplied", (await database.Context.PurchaseOrderHeaders
            .Include(row => row.Status)
            .SingleAsync()).Status.Name);
    }

    [Fact]
    public async Task Draft_order_can_be_approved_then_marked_supplied()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var order = await AddOrderAsync(database.Context, setup, setup.Statuses["Draft"]);
        var supplierUpdatedAtUtc = setup.Supplier.UpdatedAtUtc;
        var controller = CreateController(
            database.Context,
            AuthorizationActionCodes.ApprovePurchaseOrder,
            AuthorizationActionCodes.SupplyPurchaseOrder);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "receiver-42")], "test"));

        var approvedResult = await controller.Approve(order.Id, null, CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(approvedResult);
        Assert.Equal("Approved", (await database.Context.PurchaseOrderHeaders
            .Include(row => row.Status)
            .SingleAsync()).Status.Name);

        var suppliedResult = await controller.Supply(order.Id, null, CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(suppliedResult);
        Assert.Equal("Supplied", (await database.Context.PurchaseOrderHeaders
            .Include(row => row.Status)
            .SingleAsync()).Status.Name);
        var suppliedOrder = await database.Context.PurchaseOrderHeaders.SingleAsync();
        Assert.NotNull(suppliedOrder.ReceivedAtUtc);
        Assert.Equal("receiver-42", suppliedOrder.ReceivedBy);
        Assert.Equal(supplierUpdatedAtUtc, setup.Supplier.UpdatedAtUtc);
    }

    [Fact]
    public async Task Pdf_contains_three_A5_copies()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var order = await AddOrderAsync(database.Context, setup, setup.Statuses["Draft"]);
        var loadedOrder = await database.Context.PurchaseOrderHeaders
            .IgnoreQueryFilters()
            .Include(row => row.Supplier)
            .Include(row => row.Status)
            .Include(row => row.Tax)
            .Include(row => row.Details)
                .ThenInclude(detail => detail.Product)
            .SingleAsync(row => row.Id == order.Id);

        var pdf = new PurchaseOrderPdfService().Create(loadedOrder);
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        Assert.Equal(3, document.PageCount);
        Assert.Equal(420, document.Pages[0].Width.Point, 0);
        Assert.Equal(595, document.Pages[0].Height.Point, 0);
    }

    [Fact]
    public async Task Index_filters_by_order_number_supplier_and_status()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var order = await AddOrderAsync(database.Context, setup, setup.Statuses["Draft"]);
        var controller = CreateController(database.Context, AuthorizationActionCodes.ViewPurchaseOrder);

        async Task<int> CountAsync(string? q, int? statusId)
        {
            var result = await controller.Index(q, statusId, CancellationToken.None);
            var model = Assert.IsAssignableFrom<IReadOnlyList<PurchaseOrderHeader>>(
                Assert.IsType<ViewResult>(result).Model);
            return model.Count;
        }

        Assert.Equal(1, await CountAsync("2026-00001", null));
        Assert.Equal(1, await CountAsync(order.Supplier.Name, null));
        Assert.Equal(1, await CountAsync(null, setup.Statuses["Draft"].Id));
        Assert.Equal(0, await CountAsync(null, setup.Statuses["Approved"].Id));
        Assert.Equal(0, await CountAsync("nomatch", null));
    }

    [Fact]
    public async Task Approve_from_index_redirects_back_to_index()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var order = await AddOrderAsync(database.Context, setup, setup.Statuses["Draft"]);
        var controller = CreateController(database.Context, AuthorizationActionCodes.ApprovePurchaseOrder);

        var result = await controller.Approve(order.Id, "index", CancellationToken.None);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Fact]
    public async Task Pdf_paginates_long_orders_for_all_three_copies()
    {
        using var database = await CreateDatabaseAsync();
        var setup = await SeedPurchaseOrderLookupsAsync(database.Context);
        var order = await AddOrderAsync(database.Context, setup, setup.Statuses["Draft"]);
        for (var index = 0; index < 40; index++)
        {
            order.Details.Add(new PurchaseOrderDetail
            {
                Product = setup.Product,
                Qty = 1m,
                Price = 1m
            });
        }
        PurchaseOrderPricing.Recalculate(order);
        await database.Context.SaveChangesAsync();

        var loadedOrder = await database.Context.PurchaseOrderHeaders
            .IgnoreQueryFilters()
            .Include(row => row.Supplier)
            .Include(row => row.Status)
            .Include(row => row.Tax)
            .Include(row => row.Details)
                .ThenInclude(detail => detail.Product)
            .SingleAsync(row => row.Id == order.Id);
        using var stream = new MemoryStream(new PurchaseOrderPdfService().Create(loadedOrder));
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);

        Assert.True(document.PageCount > 3);
        Assert.Equal(0, document.PageCount % 3);
    }

    [Fact]
    public async Task Purchase_order_migration_creates_audited_tables_without_recreating_legacy_orders()
    {
        Batteries.Init();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());

        await context.Database.MigrateAsync();

        var headerColumns = await ReadColumnNamesAsync(connection, "PurchaseOrderHeaders");
        var detailColumns = await ReadColumnNamesAsync(connection, "PurchaseOrderDetails");
        Assert.Contains("OrderNumber", headerColumns);
        Assert.Contains("DiscountPercent3", headerColumns);
        Assert.Contains("CreatedAtUtc", headerColumns);
        Assert.Contains("UpdatedBy", headerColumns);
        Assert.Contains("ReceivedAtUtc", headerColumns);
        Assert.Contains("ReceivedBy", headerColumns);
        Assert.Contains("DiscountPercent", detailColumns);
        Assert.Contains("CreatedBy", detailColumns);
        Assert.False(await TableExistsAsync(connection, "Orders"));
    }

    [Fact]
    public async Task Authorization_seed_registers_purchase_order_actions()
    {
        using var database = await CreateDatabaseAsync();
        await AuthorizationSeeder.SeedAsync(
            database.Context,
            new Microsoft.AspNetCore.Identity.PasswordHasher<User>(),
            "admin@example.test",
            "initial-password");

        var codes = await database.Context.Actions.Select(action => action.Code).ToListAsync();
        Assert.Contains(AuthorizationActionCodes.ViewPurchaseOrder, codes);
        Assert.Contains(AuthorizationActionCodes.ApprovePurchaseOrder, codes);
        Assert.Contains(AuthorizationActionCodes.EditSuppliedPurchaseOrder, codes);
    }

    private static async Task<(Supplier Supplier, Tax Tax, Dictionary<string, Status> Statuses, Product Product)>
        SeedPurchaseOrderLookupsAsync(AppDbContext context)
    {
        var supplier = new Supplier
        {
            Name = "Sample Supplier",
            Address = "Supplier address",
            RFC = "RFC123",
            Bank = new Bank { Name = "Sample Bank" },
            BankAccount = "123456"
        };
        var tax = NewTax();
        var statuses = new[] { "Draft", "Approved", "Supplied", "Cancelled" }
            .Select(name => new Status { Name = name, Entity = "PurchaseOrder" })
            .ToDictionary(status => status.Name);
        var uom = new Uom { Name = "Unit" };
        var category = new ProductCategory { Name = "General", Prefix = "G" };
        var product = new Product
        {
            Name = "Sample Product",
            Code = "P0001",
            Uom = uom,
            ProductCategory = category
        };
        context.AddRange(supplier, tax);
        context.AddRange(statuses.Values);
        context.AddRange(uom, category, product);
        await context.SaveChangesAsync();
        return (supplier, tax, statuses, product);
    }

    private static async Task<PurchaseOrderHeader> AddOrderAsync(
        AppDbContext context,
        (Supplier Supplier, Tax Tax, Dictionary<string, Status> Statuses, Product Product) setup,
        Status status)
    {
        var order = new PurchaseOrderHeader
        {
            OrderNumber = "2026-00001",
            Supplier = setup.Supplier,
            SupplyAddress = "10 Main Street",
            SupplyDate = DateTime.Today.AddDays(1),
            Status = status,
            Tax = setup.Tax,
            Details =
            [
                new PurchaseOrderDetail
                {
                    Product = setup.Product,
                    Qty = 2m,
                    Price = 12.5m
                }
            ]
        };
        PurchaseOrderPricing.Recalculate(order);
        context.PurchaseOrderHeaders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    private static Tax NewTax(decimal value = 0.16m) =>
        new() { Name = "VAT", Value = value };

    private static PurchaseOrdersController CreateController(
        AppDbContext context,
        params string[] actionCodes)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddControllersWithViews()
            .Services
            .BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        return new PurchaseOrdersController(
            new UnitOfWork(context),
            context,
            new TestPermissionService(actionCodes),
            new PurchaseOrderPdfService())
        {
            ObjectValidator = services.GetRequiredService<IObjectModelValidator>(),
            Url = new StubUrlHelper(),
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        return columns;
    }

    private static async Task<bool> TableExistsAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
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

    private sealed class TestPermissionService(IEnumerable<string> actionCodes) : IActionPermissionService
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

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/PurchaseOrders";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/PurchaseOrders";
        public string? RouteUrl(UrlRouteContext routeContext) => "/PurchaseOrders";
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
