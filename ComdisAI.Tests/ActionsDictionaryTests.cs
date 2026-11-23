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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class ActionsDictionaryTests
{
    [Fact]
    public void Action_requires_code_and_description_with_specified_lengths()
    {
        var action = NewAction(string.Empty, new string('D', 256));
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(action, new ValidationContext(action), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Actions.Code)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Actions.Description)));
    }

    [Fact]
    public async Task Create_trims_fields_and_rejects_active_and_deleted_duplicate_codes()
    {
        using var database = await CreateDatabaseAsync();
        var create = await CreateController(database.Context).Create(
            NewAction("  CUSTOM_ONE  ", "  Custom action  "),
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(create);
        var action = await database.Context.Actions.SingleAsync();
        Assert.Equal("CUSTOM_ONE", action.Code);
        Assert.Equal("Custom action", action.Description);
        Assert.False(action.IsSystem);

        var duplicate = await CreateController(database.Context).Create(
            NewAction("custom_one", "Duplicate"),
            CancellationToken.None);
        Assert.Contains(
            Assert.IsType<ViewResult>(duplicate).ViewData.ModelState[nameof(Actions.Code)]!.Errors,
            error => error.ErrorMessage.Contains("already exists"));

        action.IsDeleted = true;
        await database.Context.SaveChangesAsync();
        var deletedDuplicate = await CreateController(database.Context).Create(
            NewAction("CUSTOM_ONE", "Duplicate deleted"),
            CancellationToken.None);
        Assert.False(Assert.IsType<ViewResult>(deletedDuplicate).ViewData.ModelState.IsValid);

        using var emptyDatabase = await CreateDatabaseAsync();
        var requiredCode = await CreateController(emptyDatabase.Context).Create(
            NewAction(AuthorizationActionCodes.ViewAction, "Forged system action"),
            CancellationToken.None);
        Assert.False(Assert.IsType<ViewResult>(requiredCode).ViewData.ModelState.IsValid);
    }

    [Fact]
    public async Task Edit_updates_custom_action_and_delete_is_logical_with_audit()
    {
        using var database = await CreateDatabaseAsync();
        var action = NewAction("CUSTOM_OLD", "Old description");
        database.Context.Actions.Add(action);
        await database.Context.SaveChangesAsync();

        var edit = await CreateController(database.Context).Edit(
            action.Id,
            NewAction("  CUSTOM_NEW  ", "  New description  "),
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(edit);
        Assert.Equal("CUSTOM_NEW", action.Code);
        Assert.Equal("New description", action.Description);

        var delete = await CreateController(database.Context).Delete(action.Id, CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(delete);
        Assert.Empty(await database.Context.Actions.ToListAsync());
        Assert.True(action.IsDeleted);
        Assert.NotNull(action.DeletedAtUtc);
        Assert.Equal("system", action.DeletedBy);
    }

    [Fact]
    public async Task Deleted_ids_return_not_found_for_get_post_edit_and_delete()
    {
        using var database = await CreateDatabaseAsync();
        var action = NewAction("CUSTOM_DELETED", "Deleted action");
        database.Context.Actions.Add(action);
        await database.Context.SaveChangesAsync();
        action.IsDeleted = true;
        await database.Context.SaveChangesAsync();

        Assert.IsType<NotFoundResult>(
            await CreateController(database.Context).Edit(action.Id, CancellationToken.None));
        Assert.IsType<NotFoundResult>(
            await CreateController(database.Context).Edit(
                action.Id,
                NewAction(string.Empty, string.Empty),
                CancellationToken.None));
        Assert.IsType<NotFoundResult>(
            await CreateController(database.Context).Delete(action.Id, CancellationToken.None));
    }

    [Fact]
    public async Task System_action_allows_description_changes_but_not_code_or_delete_changes()
    {
        using var database = await CreateDatabaseAsync();
        var action = NewAction(AuthorizationActionCodes.ViewAction, "View actions", isSystem: true);
        var role = new Role { Code = "ADMIN_TEST", Description = "Admin test" };
        database.Context.AddRange(action, role);
        await database.Context.SaveChangesAsync();
        database.Context.RoleActions.Add(new RoleAction { Role = role, Action = action });
        await database.Context.SaveChangesAsync();

        var caseOnlyEdit = await CreateController(database.Context).Edit(
            action.Id,
            NewAction(AuthorizationActionCodes.ViewAction.ToLowerInvariant(), "  Updated description  "),
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(caseOnlyEdit);
        Assert.Equal(AuthorizationActionCodes.ViewAction, action.Code);
        Assert.Equal("Updated description", action.Description);

        var forgedEdit = await CreateController(database.Context).Edit(
            action.Id,
            NewAction("VIEW_EVERYTHING", "Forged description"),
            CancellationToken.None);
        var view = Assert.IsType<ViewResult>(forgedEdit);
        Assert.Equal("Forged description", ((Actions)view.Model!).Description);
        Assert.Contains(view.ViewData.ModelState[nameof(Actions.Code)]!.Errors,
            error => error.ErrorMessage.Contains("cannot be changed"));
        Assert.Equal(AuthorizationActionCodes.ViewAction, action.Code);
        Assert.Equal("Updated description", action.Description);

        Assert.IsType<ConflictObjectResult>(
            await CreateController(database.Context).Delete(action.Id, CancellationToken.None));
        Assert.False(action.IsDeleted);
        Assert.Null(action.DeletedAtUtc);
        Assert.Single(await database.Context.RoleActions.ToListAsync());
    }

    [Fact]
    public async Task Context_blocks_system_action_mutations_and_incorrect_classification()
    {
        using var database = await CreateDatabaseAsync();
        var systemAction = NewAction(AuthorizationActionCodes.ViewAction, "View actions", isSystem: true);
        var customAction = NewAction("CUSTOM_ACTION", "Custom action");
        database.Context.AddRange(systemAction, customAction);
        await database.Context.SaveChangesAsync();

        systemAction.Code = "REPURPOSED_PERMISSION";
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();
        systemAction = await database.Context.Actions.SingleAsync(action => action.IsSystem);

        systemAction.IsSystem = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();
        systemAction = await database.Context.Actions.SingleAsync(action => action.IsSystem);

        systemAction.IsDeleted = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();
        systemAction = await database.Context.Actions.SingleAsync(action => action.IsSystem);

        database.Context.Actions.Remove(systemAction);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();
        customAction = await database.Context.Actions.SingleAsync(action => !action.IsSystem);

        customAction.IsSystem = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Seeder_classifies_all_required_actions_and_promotes_a_legacy_match()
    {
        using var database = await CreateDatabaseAsync();
        await InsertActionAsync(
            database.Context,
            AuthorizationActionCodes.ViewHome,
            isSystem: false,
            isDeleted: false);

        await AuthorizationSeeder.SeedAsync(
            database.Context,
            new PasswordHasher<User>(),
            "admin@example.test",
            "initial-password");
        await AuthorizationSeeder.SeedAsync(
            database.Context,
            new PasswordHasher<User>(),
            "admin@example.test",
            "different-password");

        Assert.Equal(
            AuthorizationActionCodes.RequiredCodes.Order().ToArray(),
            AuthorizationSeeder.RequiredActions.Select(action => action.Code).Order().ToArray());
        var actions = await database.Context.Actions.ToListAsync();
        Assert.Equal(AuthorizationActionCodes.RequiredCodes.Count, actions.Count);
        Assert.All(actions, action => Assert.True(action.IsSystem));
        Assert.Single(actions, action => action.Code == AuthorizationActionCodes.ViewHome);
    }

    [Fact]
    public async Task Seeder_rejects_a_deleted_required_action()
    {
        using var database = await CreateDatabaseAsync();
        await InsertActionAsync(
            database.Context,
            AuthorizationActionCodes.ViewHome,
            isSystem: true,
            isDeleted: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => AuthorizationSeeder.SeedAsync(
            database.Context,
            new PasswordHasher<User>(),
            "admin@example.test",
            "initial-password"));
    }

    [Fact]
    public async Task System_action_migration_backfills_every_required_code_and_down_removes_column()
    {
        Batteries.Init();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());
        var migrator = context.GetService<IMigrator>();
        const string beforeSystemActionMigration = "20261116233049_SalesOrderDeliveryState";
        await migrator.MigrateAsync(beforeSystemActionMigration);

        foreach (var code in AuthorizationActionCodes.RequiredCodes)
            await InsertLegacyActionAsync(connection, code, isDeleted: false);
        await InsertLegacyActionAsync(connection, "CUSTOM_LEGACY", isDeleted: false);

        await migrator.MigrateAsync();
        var requiredActions = await context.Actions
            .IgnoreQueryFilters()
            .Where(action => action.IsSystem)
            .Select(action => action.Code)
            .ToListAsync();
        Assert.Equal(AuthorizationActionCodes.RequiredCodes.Order().ToArray(), requiredActions.Order().ToArray());
        var customLegacy = await context.Actions.SingleAsync(action => action.Code == "CUSTOM_LEGACY");
        Assert.False(customLegacy.IsSystem);

        Assert.Contains("IsSystem", await ReadColumnNamesAsync(connection, "Action"));
        await context.Database.CloseConnectionAsync();
        await migrator.MigrateAsync(beforeSystemActionMigration);
        await context.Database.OpenConnectionAsync();
        Assert.DoesNotContain("IsSystem", await ReadColumnNamesAsync(connection, "Action"));
    }

    [Fact]
    public async Task Create_uniqueness_race_returns_validation_error_without_contaminating_tracking()
    {
        var interceptor = new UniquenessRaceInterceptor();
        using var database = await CreateDatabaseAsync(interceptor);
        interceptor.Enable("RACE_CREATE", "Winning action");

        var result = await CreateController(database.Context).Create(
            NewAction("RACE_CREATE", "Submitted action"),
            CancellationToken.None);

        Assert.Contains(
            Assert.IsType<ViewResult>(result).ViewData.ModelState[nameof(Actions.Code)]!.Errors,
            error => error.ErrorMessage.Contains("already exists"));
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Actions.SingleAsync(action => action.Code == "RACE_CREATE");
        Assert.Equal("Winning action", stored.Description);
    }

    [Fact]
    public async Task Edit_uniqueness_race_preserves_original_action_without_contaminating_tracking()
    {
        var interceptor = new UniquenessRaceInterceptor();
        using var database = await CreateDatabaseAsync(interceptor);
        var original = NewAction("ORIGINAL", "Original action");
        database.Context.Actions.Add(original);
        await database.Context.SaveChangesAsync();
        interceptor.Enable("RACE_EDIT", "Winning action");

        var result = await CreateController(database.Context).Edit(
            original.Id,
            NewAction("RACE_EDIT", "Submitted edit"),
            CancellationToken.None);

        Assert.False(Assert.IsType<ViewResult>(result).ViewData.ModelState.IsValid);
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Actions.OrderBy(action => action.Id).ToListAsync();
        Assert.Equal("ORIGINAL", stored[0].Code);
        Assert.Equal("Original action", stored[0].Description);
        Assert.Equal("RACE_EDIT", stored[1].Code);
        Assert.Equal("Winning action", stored[1].Description);
    }

    private static Actions NewAction(string code, string description, bool isSystem = false) =>
        new() { Code = code, Description = description, IsSystem = isSystem };

    private static ActionsController CreateController(AppDbContext context)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddControllersWithViews()
            .Services
            .BuildServiceProvider();
        return new ActionsController(new UnitOfWork(context), context)
        {
            ObjectValidator = services.GetRequiredService<IObjectModelValidator>(),
            Url = new StubUrlHelper(),
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
    }

    private static async Task<TestDatabase> CreateDatabaseAsync(IInterceptor? interceptor = null)
    {
        Batteries.Init();
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
        if (interceptor is not null)
            optionsBuilder.AddInterceptors(interceptor);
        var context = new AppDbContext(optionsBuilder.Options, new HttpContextAccessor());
        await context.Database.EnsureCreatedAsync();
        return new TestDatabase(connection, context);
    }

    private static async Task InsertActionAsync(
        AppDbContext context,
        string code,
        bool isSystem,
        bool isDeleted)
    {
        var connection = context.Database.GetDbConnection();
        await InsertRawActionAsync(connection, code, isSystem, isDeleted);
        context.ChangeTracker.Clear();
    }

    private static async Task InsertLegacyActionAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string code,
        bool isDeleted) =>
        await InsertRawActionAsync(connection, code, isSystem: null, isDeleted);

    private static async Task InsertRawActionAsync(
        System.Data.Common.DbConnection connection,
        string code,
        bool? isSystem,
        bool isDeleted,
        string? description = null)
    {
        await using var command = connection.CreateCommand();
        var columns = isSystem.HasValue
            ? "Code, Description, IsDeleted, NormalizedCode, CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy, IsSystem"
            : "Code, Description, IsDeleted, NormalizedCode, CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy";
        var values = isSystem.HasValue
            ? "$code, $description, $isDeleted, $normalizedCode, $now, 'raw', $now, 'raw', $isSystem"
            : "$code, $description, $isDeleted, $normalizedCode, $now, 'raw', $now, 'raw'";
        command.CommandText = $"INSERT INTO Action ({columns}) VALUES ({values});";
        AddParameter(command, "$code", code);
        AddParameter(command, "$description", description ?? $"{code} action");
        AddParameter(command, "$isDeleted", isDeleted);
        AddParameter(command, "$normalizedCode", code.Trim().ToUpperInvariant());
        AddParameter(command, "$now", DateTime.UtcNow);
        if (isSystem.HasValue)
            AddParameter(command, "$isSystem", isSystem.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
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

    private sealed class UniquenessRaceInterceptor : SaveChangesInterceptor
    {
        private string? _code;
        private string? _description;

        public void Enable(string code, string description)
        {
            _code = code;
            _description = description;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_code is null)
                return await base.SavingChangesAsync(eventData, result, cancellationToken);

            var code = _code;
            var description = _description!;
            _code = null;
            await InsertRawActionAsync(
                eventData.Context!.Database.GetDbConnection(),
                code,
                false,
                false,
                description);
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/Actions/Index";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/Actions";
        public string? RouteUrl(UrlRouteContext routeContext) => "/Actions";
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
