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

public sealed class ProductCategoryDictionaryTests
{
    [Fact]
    public void Category_requires_a_name_and_one_character_prefix()
    {
        var category = new ProductCategory { Name = string.Empty, Prefix = "PP" };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(category, new ValidationContext(category), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(ProductCategory.Name)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(ProductCategory.Prefix)));
    }

    [Fact]
    public async Task Category_values_are_unique_case_insensitively_and_delete_is_logical_with_audit()
    {
        using var database = await CreateDatabaseAsync();
        var category = NewCategory("Paper", "P");
        database.Context.ProductCategories.Add(category);
        await database.Context.SaveChangesAsync();

        Assert.Equal("system", category.CreatedBy);
        Assert.Equal("system", category.UpdatedBy);
        Assert.NotEqual(default, category.CreatedAtUtc);
        Assert.NotEqual(default, category.UpdatedAtUtc);

        database.Context.ProductCategories.Add(NewCategory("paper", "X"));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();

        database.Context.ProductCategories.Add(NewCategory("Plastic", "p"));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();

        category = await database.Context.ProductCategories
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == category.Id);
        category.IsDeleted = true;
        await database.Context.SaveChangesAsync();
        Assert.Empty(await database.Context.ProductCategories.ToListAsync());
        Assert.True(category.DeletedAtUtc.HasValue);
        Assert.Equal("system", category.DeletedBy);
        Assert.True(await database.Context.ProductCategories
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.Id == category.Id));

        database.Context.ProductCategories.Add(NewCategory("paper", "p"));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Create_trims_category_fields_and_rejects_duplicate_name_or_prefix()
    {
        using var database = await CreateDatabaseAsync();
        var controller = CreateController(database.Context);

        var created = await controller.Create(
            new ProductCategory { Name = "  Paper  ", Prefix = " P " },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(created);
        var category = await database.Context.ProductCategories.SingleAsync();
        Assert.Equal("Paper", category.Name);
        Assert.Equal("P", category.Prefix);

        var duplicateName = await CreateController(database.Context).Create(
            new ProductCategory { Name = "paper", Prefix = "X" },
            CancellationToken.None);
        Assert.False(Assert.IsType<ViewResult>(duplicateName).ViewData.ModelState.IsValid);

        var duplicatePrefix = await CreateController(database.Context).Create(
            new ProductCategory { Name = "Plastic", Prefix = "p" },
            CancellationToken.None);
        Assert.False(Assert.IsType<ViewResult>(duplicatePrefix).ViewData.ModelState.IsValid);
        Assert.Single(await database.Context.ProductCategories.ToListAsync());
    }

    [Fact]
    public async Task Edit_updates_a_category_and_delete_keeps_it_as_a_logically_deleted_record()
    {
        using var database = await CreateDatabaseAsync();
        var category = NewCategory("Paper", "P");
        database.Context.ProductCategories.Add(category);
        await database.Context.SaveChangesAsync();

        var editResult = await CreateController(database.Context).Edit(
            category.Id,
            NewCategory("Plastic", "L"),
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(editResult);
        Assert.Equal("Plastic", category.Name);
        Assert.Equal("L", category.Prefix);

        var deleteResult = await CreateController(database.Context).Delete(category.Id, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Empty(await database.Context.ProductCategories.ToListAsync());
        var deletedCategory = await database.Context.ProductCategories
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == category.Id);
        Assert.True(deletedCategory.IsDeleted);
        Assert.NotNull(deletedCategory.DeletedAtUtc);
        Assert.Equal("system", deletedCategory.DeletedBy);
    }

    [Fact]
    public async Task Product_category_migration_creates_the_audited_table_and_unique_indexes()
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
        command.CommandText = "PRAGMA table_info('ProductCategories');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));

        Assert.Contains("Name", columns);
        Assert.Contains("Prefix", columns);
        Assert.Contains("CreatedAtUtc", columns);
        Assert.Contains("CreatedBy", columns);
        Assert.Contains("UpdatedAtUtc", columns);
        Assert.Contains("UpdatedBy", columns);
        Assert.Contains("IsDeleted", columns);
        Assert.Contains("DeletedAtUtc", columns);
        Assert.Contains("DeletedBy", columns);

        await reader.DisposeAsync();
        command.CommandText = "PRAGMA index_list('ProductCategories');";
        await using var indexReader = await command.ExecuteReaderAsync();
        var indexes = new Dictionary<string, bool>(StringComparer.Ordinal);
        while (await indexReader.ReadAsync())
            indexes.Add(indexReader.GetString(1), indexReader.GetInt64(2) == 1);

        Assert.True(indexes["IX_ProductCategories_NormalizedName"]);
        Assert.True(indexes["IX_ProductCategories_NormalizedPrefix"]);
    }

    [Fact]
    public async Task Authorization_seed_includes_product_category_actions()
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

        Assert.Contains(AuthorizationActionCodes.ViewProductCategory, actionCodes);
        Assert.Contains(AuthorizationActionCodes.CreateProductCategory, actionCodes);
        Assert.Contains(AuthorizationActionCodes.EditProductCategory, actionCodes);
        Assert.Contains(AuthorizationActionCodes.DeleteProductCategory, actionCodes);
    }

    private static ProductCategory NewCategory(string name, string prefix) =>
        new() { Name = name, Prefix = prefix };

    private static ProductCategoriesController CreateController(AppDbContext context)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddControllersWithViews()
            .Services
            .BuildServiceProvider();
        return new ProductCategoriesController(new UnitOfWork(context), context)
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
        public string? Action(UrlActionContext actionContext) => "/ProductCategories/Index";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/ProductCategories";
        public string? RouteUrl(UrlRouteContext routeContext) => "/ProductCategories";
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
