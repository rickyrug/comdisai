using System.Globalization;
using System.Security.Claims;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class AuthorizationLayerTests
{
    [Fact]
    public async Task Permission_checks_use_only_the_selected_active_role()
    {
        using var database = await CreateDatabaseAsync();
        var user = NewUser();
        var roleOne = NewRole("ONE");
        var roleTwo = NewRole("TWO");
        var actionOne = NewAction("VIEW_BANK");
        var actionTwo = NewAction("VIEW_CUSTOMER");
        database.Context.AddRange(user, roleOne, roleTwo, actionOne, actionTwo);
        await database.Context.SaveChangesAsync();
        database.Context.UserRoles.AddRange(
            new UserRole { UserId = user.Id, RoleId = roleOne.Id },
            new UserRole { UserId = user.Id, RoleId = roleTwo.Id });
        database.Context.RoleActions.AddRange(
            new RoleAction { RoleId = roleOne.Id, ActionId = actionOne.Id },
            new RoleAction { RoleId = roleTwo.Id, ActionId = actionTwo.Id });
        await database.Context.SaveChangesAsync();

        var service = new ActionPermissionService(database.Context);
        var roleOnePrincipal = Principal(user.Id, roleOne.Id);
        var roleTwoPrincipal = Principal(user.Id, roleTwo.Id);

        Assert.True(await service.HasActionAsync(roleOnePrincipal, "VIEW_BANK"));
        Assert.False(await service.HasActionAsync(roleOnePrincipal, "VIEW_CUSTOMER"));
        Assert.True(await service.HasActionAsync(roleTwoPrincipal, "VIEW_CUSTOMER"));
        Assert.Equal(new[] { "VIEW_BANK" }, await service.GetActiveActionCodesAsync(roleOnePrincipal));
    }

    [Fact]
    public async Task Permission_checks_reject_deleted_roles_and_assignments()
    {
        using var database = await CreateDatabaseAsync();
        var user = NewUser();
        var role = NewRole("DELETED");
        var action = NewAction("VIEW_BANK");
        database.Context.AddRange(user, role, action);
        await database.Context.SaveChangesAsync();
        database.Context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        database.Context.RoleActions.Add(new RoleAction { RoleId = role.Id, ActionId = action.Id });
        await database.Context.SaveChangesAsync();

        role.IsDeleted = true;
        await database.Context.SaveChangesAsync();

        var service = new ActionPermissionService(database.Context);
        Assert.False(await service.HasActionAsync(Principal(user.Id, role.Id), "VIEW_BANK"));
        Assert.Empty(await service.GetActiveRolesAsync(user.Id));
    }

    [Fact]
    public async Task Admin_seed_is_idempotent_and_does_not_reset_an_existing_password()
    {
        using var database = await CreateDatabaseAsync();
        var passwordHasher = new PasswordHasher<User>();
        await AuthorizationSeeder.SeedAsync(
            database.Context,
            passwordHasher,
            "admin@example.test",
            "first-initial-password");

        var user = await database.Context.Users.SingleAsync();
        var originalHash = user.PasswordHash;
        var actionCount = await database.Context.Actions.CountAsync();
        var assignmentCount = await database.Context.RoleActions.CountAsync();

        await AuthorizationSeeder.SeedAsync(
            database.Context,
            passwordHasher,
            "admin@example.test",
            "different-later-password");

        var seededUser = await database.Context.Users.SingleAsync();
        Assert.Equal(originalHash, seededUser.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(seededUser, seededUser.PasswordHash, "first-initial-password"));
        Assert.Equal(actionCount, await database.Context.Actions.CountAsync());
        Assert.Equal(assignmentCount, await database.Context.RoleActions.CountAsync());
        Assert.Single(await database.Context.UserRoles.ToListAsync());
    }

    private static ClaimsPrincipal Principal(int userId, int roleId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)),
            new Claim(AuthorizationClaims.ActiveRoleId, roleId.ToString(CultureInfo.InvariantCulture))
        ],
        "test"));

    private static User NewUser() =>
        new()
        {
            Name = "Test",
            Surname = "User",
            Email = "test@example.test",
            PasswordHash = "hash"
        };

    private static Role NewRole(string code) =>
        new()
        {
            Code = code,
            Description = $"{code} role"
        };

    private static Actions NewAction(string code) =>
        new()
        {
            Code = code,
            Description = $"{code} action"
        };

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
