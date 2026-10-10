using ComdisAI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ComdisAI.Authorization;

public sealed class ActionAuthorizationFilter(
    IActionPermissionService permissionService,
    string actionCode) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (await permissionService.HasActionAsync(
                context.HttpContext.User,
                actionCode,
                context.HttpContext.RequestAborted))
        {
            return;
        }

        context.Result = new ViewResult
        {
            ViewName = "/Views/Shared/AccessDenied.cshtml",
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
