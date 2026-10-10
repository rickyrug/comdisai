using ComdisAI.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Data;

public static class AuthorizationSeeder
{
    public const string DefaultAdminEmail = "admin@comdisai.local";
    public const string AdminRoleCode = "ADMIN";

    private static readonly (string Code, string Description)[] SeedActions =
    [
        (AuthorizationActionCodes.ViewHome, "View the home page"),
        (AuthorizationActionCodes.ViewPrivacy, "View the privacy page"),
        (AuthorizationActionCodes.ViewCustomer, "View customers"),
        (AuthorizationActionCodes.CreateCustomer, "Create customers"),
        (AuthorizationActionCodes.EditCustomer, "Edit customers"),
        (AuthorizationActionCodes.DeleteCustomer, "Delete customers"),
        (AuthorizationActionCodes.ViewUom, "View units of measure"),
        (AuthorizationActionCodes.CreateUom, "Create units of measure"),
        (AuthorizationActionCodes.EditUom, "Edit units of measure"),
        (AuthorizationActionCodes.DeleteUom, "Delete units of measure"),
        (AuthorizationActionCodes.ViewBank, "View banks"),
        (AuthorizationActionCodes.CreateBank, "Create banks"),
        (AuthorizationActionCodes.EditBank, "Edit banks"),
        (AuthorizationActionCodes.DeleteBank, "Delete banks"),
        (AuthorizationActionCodes.ViewAction, "View actions"),
        (AuthorizationActionCodes.CreateAction, "Create actions"),
        (AuthorizationActionCodes.EditAction, "Edit actions"),
        (AuthorizationActionCodes.DeleteAction, "Delete actions"),
        (AuthorizationActionCodes.ViewRole, "View roles"),
        (AuthorizationActionCodes.CreateRole, "Create roles"),
        (AuthorizationActionCodes.EditRole, "Edit roles"),
        (AuthorizationActionCodes.DeleteRole, "Delete roles"),
        (AuthorizationActionCodes.ManageRoleActions, "Manage role actions"),
        (AuthorizationActionCodes.ViewUser, "View users"),
        (AuthorizationActionCodes.CreateUser, "Create users"),
        (AuthorizationActionCodes.EditUser, "Edit users"),
        (AuthorizationActionCodes.DeleteUser, "Delete users"),
        (AuthorizationActionCodes.AssignUserRoles, "Assign user roles"),
        (AuthorizationActionCodes.ResetUserPassword, "Reset user passwords")
    ];

    public static async Task SeedAsync(
        AppDbContext dbContext,
        IPasswordHasher<User> passwordHasher,
        string adminEmail,
        string initialPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adminEmail))
            throw new ArgumentException("An admin email is required.", nameof(adminEmail));
        if (string.IsNullOrWhiteSpace(initialPassword))
            throw new ArgumentException("An initial admin password is required.", nameof(initialPassword));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rolesByCodeRows = await dbContext.Roles
            .IgnoreQueryFilters()
            .Select(role => new
            {
                Role = role,
                NormalizedCode = EF.Property<string>(role, "NormalizedCode")
            })
            .ToListAsync(cancellationToken);
        var rolesByCode = rolesByCodeRows.ToDictionary(row => row.NormalizedCode, row => row.Role);
        var normalizedAdminRoleCode = AdminRoleCode.ToUpperInvariant();
        if (rolesByCode.TryGetValue(normalizedAdminRoleCode, out var existingAdminRole) &&
            existingAdminRole.IsDeleted)
        {
            throw new InvalidOperationException(
                "The configured admin role exists but is logically deleted. Restore it before startup.");
        }

        var adminRole = existingAdminRole ?? new Role
        {
            Code = AdminRoleCode,
            Description = "System administrator"
        };
        if (existingAdminRole is null)
            await dbContext.Roles.AddAsync(adminRole, cancellationToken);

        var actionRows = await dbContext.Actions
            .IgnoreQueryFilters()
            .Select(action => new
            {
                Action = action,
                NormalizedCode = EF.Property<string>(action, "NormalizedCode")
            })
            .ToListAsync(cancellationToken);
        var actionsByCode = actionRows.ToDictionary(row => row.NormalizedCode, row => row.Action);
        foreach (var (code, description) in SeedActions)
        {
            var normalizedCode = code.ToUpperInvariant();
            if (actionsByCode.TryGetValue(normalizedCode, out var existingAction))
            {
                if (existingAction.IsDeleted)
                {
                    throw new InvalidOperationException(
                        $"The required authorization action '{code}' exists but is logically deleted.");
                }

                continue;
            }

            var action = new Actions { Code = code, Description = description };
            await dbContext.Actions.AddAsync(action, cancellationToken);
            actionsByCode.Add(normalizedCode, action);
        }

        var normalizedAdminEmail = User.NormalizeEmail(adminEmail);
        var existingAdmin = await dbContext.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                user => EF.Property<string>(user, "NormalizedEmail") == normalizedAdminEmail,
                cancellationToken);
        if (existingAdmin is { IsDeleted: true })
        {
            throw new InvalidOperationException(
                "The configured admin user exists but is logically deleted. Restore it before startup.");
        }

        User? adminUser = existingAdmin;
        if (adminUser is null)
        {
            adminUser = new User
            {
                Name = "System",
                Surname = "Administrator",
                Email = adminEmail.Trim(),
                PasswordHash = string.Empty
            };
            adminUser.PasswordHash = passwordHasher.HashPassword(adminUser, initialPassword);
            await dbContext.Users.AddAsync(adminUser, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var roleActionIds = await dbContext.RoleActions
            .IgnoreQueryFilters()
            .Where(roleAction => roleAction.RoleId == adminRole.Id)
            .Select(roleAction => roleAction.ActionId)
            .ToListAsync(cancellationToken);
        var assignedActionIds = roleActionIds.ToHashSet();
        await dbContext.RoleActions.AddRangeAsync(
            actionsByCode.Values
                .Where(action => !assignedActionIds.Contains(action.Id))
                .Select(action => new RoleAction
                {
                    RoleId = adminRole.Id,
                    ActionId = action.Id
                }),
            cancellationToken);

        var hasAdminAssignment = await dbContext.UserRoles
            .IgnoreQueryFilters()
            .AnyAsync(
                userRole => userRole.UserId == adminUser.Id && userRole.RoleId == adminRole.Id,
                cancellationToken);
        if (!hasAdminAssignment)
        {
            await dbContext.UserRoles.AddAsync(
                new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id },
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
