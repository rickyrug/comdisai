using System.ComponentModel.DataAnnotations;
using ComdisAI.Controllers;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
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

public sealed class TaxDictionaryTests
{
    [Fact]
    public void Tax_requires_a_name_with_the_specified_length()
    {
        var tax = new Tax { Name = new string('T', 256), Value = 0.1600m };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(tax, new ValidationContext(tax), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Tax.Name)));
    }

    [Fact]
    public async Task Tax_names_are_unique_case_insensitively_and_audit_and_logical_delete_are_applied()
    {
        using var database = await CreateDatabaseAsync();
        var tax = NewTax("VAT", 0.1600m);
        database.Context.Taxes.Add(tax);
        await database.Context.SaveChangesAsync();

        Assert.Equal("system", tax.CreatedBy);
        Assert.Equal("system", tax.UpdatedBy);
        Assert.NotEqual(default, tax.CreatedAtUtc);
        Assert.NotEqual(default, tax.UpdatedAtUtc);

        database.Context.Taxes.Add(NewTax("vat", 0.1800m));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();

        tax = await database.Context.Taxes.SingleAsync();
        tax.IsDeleted = true;
        await database.Context.SaveChangesAsync();

        Assert.True(tax.DeletedAtUtc.HasValue);
        Assert.Equal("system", tax.DeletedBy);
        Assert.Empty(await database.Context.Taxes.ToListAsync());
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            database.Context.Taxes.Add(NewTax("Vat", 0.1600m));
            await database.Context.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Create_and_edit_trim_the_name_and_reject_duplicate_names()
    {
        using var database = await CreateDatabaseAsync();
        var created = await CreateController(database.Context).Create(
            NewTax("  VAT  ", 0.1600m),
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(created);
        var tax = await database.Context.Taxes.SingleAsync();
        Assert.Equal("VAT", tax.Name);
        Assert.Equal(0.1600m, tax.Value);

        var duplicate = await CreateController(database.Context).Create(
            NewTax("vat", 0.1800m),
            CancellationToken.None);
        Assert.False(Assert.IsType<ViewResult>(duplicate).ViewData.ModelState.IsValid);

        var editResult = await CreateController(database.Context).Edit(
            tax.Id,
            NewTax("  Sales Tax  ", 0.0825m),
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(editResult);
        Assert.Equal("Sales Tax", tax.Name);
        Assert.Equal(0.0825m, tax.Value);

        var deleteResult = await CreateController(database.Context)
            .Delete(tax.Id, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.True(tax.IsDeleted);
        Assert.NotNull(tax.DeletedAtUtc);
        Assert.Equal("system", tax.DeletedBy);
        Assert.Empty(await database.Context.Taxes.ToListAsync());
    }

    [Fact]
    public async Task Create_rejects_values_outside_decimal_precision_and_scale()
    {
        foreach (var value in new[] { 0.12345m, 100_000_000_000_000m })
        {
            using var database = await CreateDatabaseAsync();
            var result = await CreateController(database.Context).Create(
                NewTax("Invalid Tax", value),
                CancellationToken.None);

            var view = Assert.IsType<ViewResult>(result);
            Assert.Contains(nameof(Tax.Value), view.ViewData.ModelState.Keys);
            Assert.Empty(await database.Context.Taxes.ToListAsync());
        }
    }

    [Fact]
    public async Task Tax_migration_creates_the_audited_table_and_unique_index()
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
        command.CommandText = "PRAGMA table_info('Taxes');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));

        Assert.Contains("Name", columns);
        Assert.Contains("Value", columns);
        Assert.Contains("CreatedAtUtc", columns);
        Assert.Contains("CreatedBy", columns);
        Assert.Contains("UpdatedAtUtc", columns);
        Assert.Contains("UpdatedBy", columns);
        Assert.Contains("IsDeleted", columns);
        Assert.Contains("DeletedAtUtc", columns);
        Assert.Contains("DeletedBy", columns);

        await reader.DisposeAsync();
        command.CommandText = "PRAGMA index_list('Taxes');";
        await using var indexReader = await command.ExecuteReaderAsync();
        var indexes = new Dictionary<string, bool>(StringComparer.Ordinal);
        while (await indexReader.ReadAsync())
            indexes.Add(indexReader.GetString(1), indexReader.GetInt64(2) == 1);

        Assert.True(indexes["IX_Taxes_NormalizedName"]);
        var valueProperty = context.Model.FindEntityType(typeof(Tax))!
            .FindProperty(nameof(Tax.Value))!;
        Assert.Equal(18, valueProperty.GetPrecision());
        Assert.Equal(4, valueProperty.GetScale());
    }

    [Fact]
    public async Task Authorization_seed_includes_tax_actions()
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

        Assert.Contains(AuthorizationActionCodes.ViewTax, actionCodes);
        Assert.Contains(AuthorizationActionCodes.CreateTax, actionCodes);
        Assert.Contains(AuthorizationActionCodes.EditTax, actionCodes);
        Assert.Contains(AuthorizationActionCodes.DeleteTax, actionCodes);
    }

    private static Tax NewTax(string name, decimal value) =>
        new() { Name = name, Value = value };

    private static TaxesController CreateController(AppDbContext context)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddControllersWithViews()
            .Services
            .BuildServiceProvider();
        return new TaxesController(new UnitOfWork(context), context)
        {
            ObjectValidator = services.GetRequiredService<IObjectModelValidator>(),
            Url = new StubUrlHelper(),
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/Taxes/Index";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/Taxes";
        public string? RouteUrl(UrlRouteContext routeContext) => "/Taxes";
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
