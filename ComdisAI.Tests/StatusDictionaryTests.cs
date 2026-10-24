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

public sealed class StatusDictionaryTests
{
    [Fact]
    public void Status_requires_a_name_and_entity_with_the_specified_lengths()
    {
        var status = new Status { Name = string.Empty, Entity = new string('E', 21) };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(status, new ValidationContext(status), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Status.Name)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Status.Entity)));
    }

    [Fact]
    public async Task Status_names_are_unique_case_insensitively_per_entity_and_audit_is_applied()
    {
        using var database = await CreateDatabaseAsync();
        var status = NewStatus("Open", "SalesOrder");
        database.Context.Statuses.Add(status);
        await database.Context.SaveChangesAsync();

        Assert.Equal("system", status.CreatedBy);
        Assert.Equal("system", status.UpdatedBy);
        Assert.NotEqual(default, status.CreatedAtUtc);
        Assert.NotEqual(default, status.UpdatedAtUtc);

        database.Context.Statuses.Add(NewStatus("open", "salesorder"));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();

        database.Context.Statuses.Add(NewStatus("Open", "Customer"));
        await database.Context.SaveChangesAsync();
        Assert.Equal(2, await database.Context.Statuses.CountAsync());

        status = await database.Context.Statuses.SingleAsync(candidate => candidate.Id == status.Id);
        status.IsDeleted = true;
        await database.Context.SaveChangesAsync();
        Assert.True(status.DeletedAtUtc.HasValue);
        Assert.Equal("system", status.DeletedBy);
        Assert.Single(await database.Context.Statuses.ToListAsync());
        Assert.False(await database.Context.Statuses
            .AnyAsync(candidate => candidate.Id == status.Id));

        database.Context.Statuses.Add(NewStatus("open", "SALESORDER"));
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Create_trims_fields_and_rejects_duplicate_name_for_the_same_entity()
    {
        using var database = await CreateDatabaseAsync();
        var created = await CreateController(database.Context).Create(
            NewStatus("  Open  ", "  SalesOrder  "),
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(created);
        var status = await database.Context.Statuses.SingleAsync();
        Assert.Equal("Open", status.Name);
        Assert.Equal("SalesOrder", status.Entity);

        var duplicate = await CreateController(database.Context).Create(
            NewStatus("open", "salesorder"),
            CancellationToken.None);
        Assert.False(Assert.IsType<ViewResult>(duplicate).ViewData.ModelState.IsValid);

        var differentEntity = await CreateController(database.Context).Create(
            NewStatus("Open", "Customer"),
            CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(differentEntity);
        Assert.Equal(2, await database.Context.Statuses.CountAsync());
    }

    [Fact]
    public async Task Edit_updates_a_status_and_delete_is_logical_with_audit()
    {
        using var database = await CreateDatabaseAsync();
        var status = NewStatus("Open", "SalesOrder");
        database.Context.Statuses.Add(status);
        await database.Context.SaveChangesAsync();

        var editResult = await CreateController(database.Context).Edit(
            status.Id,
            NewStatus("Pending", "SalesOrder"),
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(editResult);
        Assert.Equal("Pending", status.Name);

        var deleteResult = await CreateController(database.Context)
            .Delete(status.Id, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(deleteResult);
        Assert.Empty(await database.Context.Statuses.ToListAsync());
        Assert.True(status.IsDeleted);
        Assert.NotNull(status.DeletedAtUtc);
        Assert.Equal("system", status.DeletedBy);
    }

    [Fact]
    public async Task Status_migration_creates_the_audited_table_and_composite_unique_index()
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
        command.CommandText = "PRAGMA table_info('Statuses');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));

        Assert.Contains("Name", columns);
        Assert.Contains("Entity", columns);
        Assert.Contains("CreatedAtUtc", columns);
        Assert.Contains("CreatedBy", columns);
        Assert.Contains("UpdatedAtUtc", columns);
        Assert.Contains("UpdatedBy", columns);
        Assert.Contains("IsDeleted", columns);
        Assert.Contains("DeletedAtUtc", columns);
        Assert.Contains("DeletedBy", columns);

        await reader.DisposeAsync();
        command.CommandText = "PRAGMA index_list('Statuses');";
        await using var indexReader = await command.ExecuteReaderAsync();
        var indexes = new Dictionary<string, bool>(StringComparer.Ordinal);
        while (await indexReader.ReadAsync())
            indexes.Add(indexReader.GetString(1), indexReader.GetInt64(2) == 1);

        Assert.True(indexes["IX_Statuses_NormalizedEntity_NormalizedName"]);
    }

    [Fact]
    public async Task Authorization_seed_includes_status_actions()
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

        Assert.Contains(AuthorizationActionCodes.ViewStatus, actionCodes);
        Assert.Contains(AuthorizationActionCodes.CreateStatus, actionCodes);
        Assert.Contains(AuthorizationActionCodes.EditStatus, actionCodes);
        Assert.Contains(AuthorizationActionCodes.DeleteStatus, actionCodes);
    }

    private static Status NewStatus(string name, string entity) =>
        new() { Name = name, Entity = entity };

    private static StatusesController CreateController(AppDbContext context)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddControllersWithViews()
            .Services
            .BuildServiceProvider();
        return new StatusesController(new UnitOfWork(context), context)
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
        public string? Action(UrlActionContext actionContext) => "/Statuses/Index";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/Statuses";
        public string? RouteUrl(UrlRouteContext routeContext) => "/Statuses";
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
