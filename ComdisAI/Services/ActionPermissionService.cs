using System.Globalization;
using System.Security.Claims;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.ViewModels.Authentication;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Services;

public sealed class ActionPermissionService(AppDbContext dbContext) : IActionPermissionService
{
    public async Task<IReadOnlyList<RoleSelectionOptionViewModel>> GetActiveRolesAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId)
            .OrderBy(userRole => userRole.Role.Code)
            .Select(userRole => new RoleSelectionOptionViewModel
            {
                Id = userRole.RoleId,
                Code = userRole.Role.Code,
                Description = userRole.Role.Description
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetActiveActionCodesAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSelectedRole(principal, out var userId, out var roleId))
            return [];

        return await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId && userRole.RoleId == roleId)
            .SelectMany(userRole => userRole.Role.RoleActions.Select(roleAction => roleAction.Action.Code))
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasActionAsync(
        ClaimsPrincipal principal,
        string actionCode,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSelectedRole(principal, out var userId, out var roleId))
            return false;

        return await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == userId && userRole.RoleId == roleId)
            .AnyAsync(
                userRole => userRole.Role.RoleActions.Any(
                    roleAction => roleAction.Action.Code == actionCode),
                cancellationToken);
    }

    private static bool TryGetSelectedRole(
        ClaimsPrincipal principal,
        out int userId,
        out int roleId)
    {
        userId = 0;
        roleId = 0;
        return int.TryParse(
                   principal.FindFirstValue(ClaimTypes.NameIdentifier),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out userId)
               && int.TryParse(
                   principal.FindFirstValue(AuthorizationClaims.ActiveRoleId),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out roleId);
    }
}
