using System.Diagnostics;
using System.Security.Claims;
using ComdisAI.Models;
using ComdisAI.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ComdisAI.Filters;

public sealed class GlobalActionLoggingFilter(ILogger<GlobalActionLoggingFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var controller = context.ActionDescriptor.RouteValues.TryGetValue("controller", out var controllerName)
            ? controllerName ?? "unknown"
            : "unknown";
        var action = context.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName)
            ? actionName ?? "unknown"
            : "unknown";
        var principal = context.HttpContext.User;
        var userId = principal.Identity?.IsAuthenticated == true
            ? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown"
            : "anonymous";
        var activeRoleId = principal.FindFirstValue(AuthorizationClaims.ActiveRoleId) ?? "none";
        var actionParameters = SerializeParameters(context.ActionArguments, controller, action);

        try
        {
            var executedContext = await next();
            stopwatch.Stop();
            var actionException = executedContext.Exception is not null && !executedContext.ExceptionHandled
                ? executedContext.Exception
                : null;
            var actionResult = executedContext.Result?.GetType().Name ?? "none";

            if (actionException is null)
            {
                logger.LogInformation(
                    "MVC action {Controller}.{Action} by user {UserId} in role {ActiveRoleId} with parameters {ActionParameters} started at {StartedAtUtc} returned {ActionResult} in {ElapsedMilliseconds} ms",
                    controller,
                    action,
                    userId,
                    activeRoleId,
                    actionParameters,
                    startedAtUtc,
                    actionResult,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogError(
                    actionException,
                    "MVC action {Controller}.{Action} by user {UserId} in role {ActiveRoleId} with parameters {ActionParameters} started at {StartedAtUtc} failed in {ElapsedMilliseconds} ms",
                    controller,
                    action,
                    userId,
                    activeRoleId,
                    actionParameters,
                    startedAtUtc,
                    stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(
                exception,
                "MVC action {Controller}.{Action} by user {UserId} in role {ActiveRoleId} with parameters {ActionParameters} started at {StartedAtUtc} failed in {ElapsedMilliseconds} ms",
                controller,
                action,
                userId,
                activeRoleId,
                actionParameters,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private string SerializeParameters(
        IDictionary<string, object?> actionArguments,
        string controller,
        string action)
    {
        try
        {
            return ActionParameterSerializer.Serialize(actionArguments);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not serialize parameters for MVC action {Controller}.{Action}",
                controller,
                action);
            return "[parameters unavailable]";
        }
    }
}
