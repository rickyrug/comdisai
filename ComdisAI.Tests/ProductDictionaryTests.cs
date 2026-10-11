using System.ComponentModel.DataAnnotations;
using ComdisAI.Controllers;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.Products;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class ProductDictionaryTests
{
    [Fact]
    public void Product_requires_a_name_and_code()
    {
        var product = new Product
        {
            Name = string.Empty,
            Code = string.Empty,
            UomId = 1
        };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(product, new ValidationContext(product), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Product.Name)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Product.Code)));
    }

    [Fact]
    public async Task Product_code_is_case_insensitively_unique_and_delete_is_logical_with_audit()
    {
        using var database = await CreateDatabaseAsync();
        var uom = NewUom("Each");
        database.Context.Uoms.Add(uom);
        await database.Context.SaveChangesAsync();

        var product = NewProduct("P-01", uom.Id);
        database.Context.Products.Add(product);
        await database.Context.SaveChangesAsync();

        Assert.Equal("system", product.CreatedBy);
        Assert.Equal("system", product.UpdatedBy);
        Assert.NotEqual(default, product.CreatedAtUtc);
        Assert.NotEqual(default, product.UpdatedAtUtc);

        product.IsDeleted = true;
        await database.Context.SaveChangesAsync();
        Assert.Empty(await database.Context.Products.ToListAsync());
        Assert.True(product.DeletedAtUtc.HasValue);
        Assert.Equal("system", product.DeletedBy);
        Assert.True(await database.Context.Products
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.Id == product.Id));

        database.Context.Products.Add(NewProduct("p-01", uom.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Product_requires_an_existing_unit_of_measure()
    {
        using var database = await CreateDatabaseAsync();
        database.Context.Products.Add(NewProduct("P-01", 123));

        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("123", "00123")]
    [InlineData("01234", "01234")]
    public async Task Create_pads_product_code_to_five_characters(string submittedCode, string expectedCode)
    {
        using var database = await CreateDatabaseAsync();
        var uom = NewUom("Each");
        database.Context.Uoms.Add(uom);
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context).Create(
            new ProductFormViewModel
            {
                Name = "Test product",
                Code = submittedCode,
                UomId = uom.Id
            },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        var product = await database.Context.Products.SingleAsync();
        Assert.Equal(expectedCode, product.Code);
    }

    [Fact]
    public async Task Create_rejects_codes_longer_than_five_characters_without_truncating()
    {
        using var database = await CreateDatabaseAsync();
        var uom = NewUom("Each");
        database.Context.Uoms.Add(uom);
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context).Create(
            new ProductFormViewModel
            {
                Name = "Test product",
                Code = "123456",
                UomId = uom.Id
            },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductFormViewModel>(view.Model);
        Assert.Equal("123456", model.Code);
        Assert.False(view.ViewData.ModelState.IsValid);
        Assert.Empty(await database.Context.Products.ToListAsync());
    }

    [Fact]
    public async Task Create_rejects_a_code_that_matches_after_zero_padding()
    {
        using var database = await CreateDatabaseAsync();
        var uom = NewUom("Each");
        database.Context.Uoms.Add(uom);
        await database.Context.SaveChangesAsync();
        database.Context.Products.Add(NewProduct("00123", uom.Id));
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context).Create(
            new ProductFormViewModel
            {
                Name = "Duplicate product",
                Code = "123",
                UomId = uom.Id
            },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.False(view.ViewData.ModelState.IsValid);
        Assert.Equal(1, await database.Context.Products.CountAsync());
    }

    [Fact]
    public async Task Edit_pads_product_code_to_five_characters()
    {
        using var database = await CreateDatabaseAsync();
        var uom = NewUom("Each");
        database.Context.Uoms.Add(uom);
        await database.Context.SaveChangesAsync();
        var product = NewProduct("12345", uom.Id);
        database.Context.Products.Add(product);
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context).Edit(
            product.Id,
            new ProductFormViewModel
            {
                Name = product.Name,
                Code = "42",
                UomId = uom.Id
            },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("00042", product.Code);
    }

    [Fact]
    public async Task Products_dictionary_migration_creates_the_specified_table_shape()
    {
        Batteries.Init();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options,
            new HttpContextAccessor());

        await context.Database.MigrateAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('Products');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));

        Assert.Contains("Name", columns);
        Assert.Contains("Code", columns);
        Assert.Contains("Uom", columns);
        Assert.Contains("CreatedAtUtc", columns);
        Assert.Contains("CreatedBy", columns);
        Assert.Contains("UpdatedAtUtc", columns);
        Assert.Contains("UpdatedBy", columns);
        Assert.DoesNotContain("Price", columns);
        Assert.DoesNotContain("Stock", columns);
        Assert.Contains(
            "20261011002627_ProductsDictionary",
            await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Create_form_offers_only_active_units_of_measure()
    {
        using var database = await CreateDatabaseAsync();
        var activeUom = NewUom("Each");
        var deletedUom = NewUom("Box");
        deletedUom.IsDeleted = true;
        database.Context.Uoms.AddRange(activeUom, deletedUom);
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context).Create(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductFormViewModel>(view.Model);
        var option = Assert.Single(model.Uoms);
        Assert.Equal(activeUom.Id.ToString(), option.Value);
        Assert.Equal(activeUom.Name, option.Text);
    }

    [Fact]
    public async Task Authorization_seed_includes_product_actions()
    {
        using var database = await CreateDatabaseAsync();
        await AuthorizationSeeder.SeedAsync(
            database.Context,
            new PasswordHasher<User>(),
            "admin@example.test",
            "initial-password");

        var actionCodes = await database.Context.Actions
            .Select(action => action.Code)
            .ToListAsync();

        Assert.Contains(AuthorizationActionCodes.ViewProduct, actionCodes);
        Assert.Contains(AuthorizationActionCodes.CreateProduct, actionCodes);
        Assert.Contains(AuthorizationActionCodes.EditProduct, actionCodes);
        Assert.Contains(AuthorizationActionCodes.DeleteProduct, actionCodes);
    }

    [Fact]
    public async Task Index_searches_case_insensitively_by_name_code_or_uom_and_hides_deleted_products()
    {
        using var database = await CreateDatabaseAsync();
        var uom = NewUom("Case");
        database.Context.Uoms.Add(uom);
        await database.Context.SaveChangesAsync();

        var matchingByName = NewProduct("A-01", uom.Id, "Steel bolt");
        var matchingByCode = NewProduct("Z-GEAR", uom.Id, "Widget");
        var deleted = NewProduct("D-01", uom.Id, "Deleted part");
        deleted.IsDeleted = true;
        database.Context.Products.AddRange(matchingByName, matchingByCode, deleted);
        await database.Context.SaveChangesAsync();

        var nameResult = await CreateController(database.Context).Index("BOLT", CancellationToken.None);
        var codeResult = await CreateController(database.Context).Index("gear", CancellationToken.None);
        var uomResult = await CreateController(database.Context).Index("case", CancellationToken.None);

        var nameModel = Assert.IsType<ProductsIndexViewModel>(Assert.IsType<ViewResult>(nameResult).Model);
        Assert.Equal("BOLT", nameModel.SearchTerm);
        Assert.Single(nameModel.Products);
        Assert.Single(Assert.IsType<ProductsIndexViewModel>(Assert.IsType<ViewResult>(codeResult).Model).Products);
        var uomMatches = Assert.IsType<ProductsIndexViewModel>(Assert.IsType<ViewResult>(uomResult).Model).Products;
        Assert.Equal(2, uomMatches.Count);
        Assert.DoesNotContain(uomMatches, product => product.IsDeleted);
    }

    private static Product NewProduct(string code, int uomId, string name = "Test product") =>
        new()
        {
            Name = name,
            Code = code,
            UomId = uomId
        };

    private static Uom NewUom(string name) =>
        new()
        {
            Name = name
        };

    private static ProductsController CreateController(AppDbContext context)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddControllersWithViews()
            .Services
            .BuildServiceProvider();
        var controller = new ProductsController(new UnitOfWork(context), context)
        {
            ObjectValidator = services.GetRequiredService<IObjectModelValidator>(),
            Url = new StubUrlHelper(),
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
        return controller;
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();

        public string? Action(UrlActionContext actionContext) => "/Products/Index";

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => true;

        public string? Link(string? routeName, object? values) => "/Products";

        public string? RouteUrl(UrlRouteContext routeContext) => "/Products";
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
