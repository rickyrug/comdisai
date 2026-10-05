using ComdisAI.Controllers;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class UserRoleAssignmentTests
{
    [Fact]
    public async Task ManageRoles_loads_active_users_roles_and_existing_assignments()
    {
        using var database = await CreateDatabaseAsync();
        var user = NewUser("active@example.test");
        var deletedUser = NewUser("deleted@example.test");
        deletedUser.IsDeleted = true;
        var activeRole = NewRole("ACTIVE");
        var deletedRole = NewRole("DELETED");
        deletedRole.IsDeleted = true;
        database.Context.AddRange(user, deletedUser, activeRole, deletedRole);
        await database.Context.SaveChangesAsync();
        database.Context.UserRoles.AddRange(
            new UserRole { UserId = user.Id, RoleId = activeRole.Id },
            new UserRole { UserId = user.Id, RoleId = deletedRole.Id });
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context)
            .ManageRoles(user.Id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ManageUserRolesViewModel>(view.Model);
        Assert.Equal(new[] { user.Id }, model.Users.Select(item => item.Id));
        Assert.Equal(new[] { activeRole.Id }, model.Roles.Select(item => item.Id));
        Assert.Equal(new[] { activeRole.Id }, model.SelectedRoleIds);
    }

    [Fact]
    public async Task ManageRoles_save_adds_checked_roles_removes_unchecked_and_keeps_deleted_role_assignments()
    {
        using var database = await CreateDatabaseAsync();
        var user = NewUser("user@example.test");
        var existingRole = NewRole("EXISTING");
        var removedRole = NewRole("REMOVED");
        var addedRole = NewRole("ADDED");
        var deletedRole = NewRole("DELETED");
        deletedRole.IsDeleted = true;
        database.Context.AddRange(user, existingRole, removedRole, addedRole, deletedRole);
        await database.Context.SaveChangesAsync();
        database.Context.UserRoles.AddRange(
            new UserRole { UserId = user.Id, RoleId = existingRole.Id },
            new UserRole { UserId = user.Id, RoleId = removedRole.Id },
            new UserRole { UserId = user.Id, RoleId = deletedRole.Id });
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context).ManageRoles(
            new ManageUserRolesViewModel
            {
                UserId = user.Id,
                SelectedRoleIds = [existingRole.Id, addedRole.Id]
            },
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        var assignments = await database.Context.UserRoles
            .IgnoreQueryFilters()
            .Where(assignment => assignment.UserId == user.Id)
            .OrderBy(assignment => assignment.RoleId)
            .ToListAsync();
        Assert.Equal(
            new[] { existingRole.Id, addedRole.Id, deletedRole.Id }.OrderBy(id => id),
            assignments.Select(assignment => assignment.RoleId));
        var newAssignment = Assert.Single(
            assignments,
            assignment => assignment.RoleId == addedRole.Id);
        Assert.Equal("system", newAssignment.CreatedBy);
        Assert.Equal("system", newAssignment.UpdatedBy);
        Assert.NotEqual(default, newAssignment.CreatedAtUtc);
        Assert.NotEqual(default, newAssignment.UpdatedAtUtc);
    }

    [Fact]
    public async Task ManageRoles_save_rejects_unavailable_roles_without_changing_assignments()
    {
        using var database = await CreateDatabaseAsync();
        var user = NewUser("user@example.test");
        var role = NewRole("AVAILABLE");
        var deletedRole = NewRole("DELETED");
        deletedRole.IsDeleted = true;
        database.Context.AddRange(user, role, deletedRole);
        await database.Context.SaveChangesAsync();
        database.Context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context).ManageRoles(
            new ManageUserRolesViewModel
            {
                UserId = user.Id,
                SelectedRoleIds = [deletedRole.Id]
            },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.False(view.ViewData.ModelState.IsValid);
        Assert.Single(await database.Context.UserRoles
            .IgnoreQueryFilters()
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task ManageRoles_save_rejects_duplicate_selected_role_ids()
    {
        using var database = await CreateDatabaseAsync();
        var user = NewUser("user@example.test");
        var role = NewRole("AVAILABLE");
        database.Context.AddRange(user, role);
        await database.Context.SaveChangesAsync();

        var result = await CreateController(database.Context).ManageRoles(
            new ManageUserRolesViewModel
            {
                UserId = user.Id,
                SelectedRoleIds = [role.Id, role.Id]
            },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.False(view.ViewData.ModelState.IsValid);
        Assert.Empty(await database.Context.UserRoles
            .IgnoreQueryFilters()
            .Where(assignment => assignment.UserId == user.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task UserRole_table_enforces_unique_user_role_pairs()
    {
        using var database = await CreateDatabaseAsync();
        var user = NewUser("user@example.test");
        var role = NewRole("ROLE");
        database.Context.AddRange(user, role);
        await database.Context.SaveChangesAsync();
        database.Context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await database.Context.SaveChangesAsync();

        database.Context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
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

    private static UsersController CreateController(AppDbContext context) =>
        new(new UnitOfWork(context), new PasswordHasher<User>(), context);

    private static User NewUser(string email) =>
        new()
        {
            Name = "Test",
            Surname = "User",
            Email = email,
            PasswordHash = "hash"
        };

    private static Role NewRole(string code) =>
        new()
        {
            Code = code,
            Description = $"{code} role"
        };

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
