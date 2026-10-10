using System.Security.Claims;
using ComdisAI.ViewModels.Authentication;

namespace ComdisAI.Services;

public interface IActionPermissionService
{
    Task<IReadOnlyList<RoleSelectionOptionViewModel>> GetActiveRolesAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetActiveActionCodesAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);

    Task<bool> HasActionAsync(
        ClaimsPrincipal principal,
        string actionCode,
        CancellationToken cancellationToken = default);
}
