using System.Diagnostics;
using System.Security.Claims;
using ComdisAI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;

namespace ComdisAI.Filters;

public sealed class UsabilityTrackingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IUsabilityEventLogger logger, IOptions<UsabilityLoggingOptions> options)
    {
        var descriptor = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (!options.Value.Enabled || descriptor is null ||
            (descriptor.ControllerName.Equals("Home", StringComparison.OrdinalIgnoreCase) && descriptor.ActionName.Equals("Error", StringComparison.OrdinalIgnoreCase)) ||
            context.Request.Path.Equals("/Error", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var principal = context.User;
        var name = principal.Identity?.IsAuthenticated == true ? principal.Identity.Name ?? "ANONYMOUS" : "ANONYMOUS";
        var userId = principal.Identity?.IsAuthenticated == true ? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? name : "ANONYMOUS";
        userId = userId.Split('\\').Last();
        var api = context.Request.Path.StartsWithSegments("/api") || descriptor.ControllerTypeInfo.IsDefined(typeof(ApiControllerAttribute), true);
        var action = descriptor.ActionName;
        var lookup = action.Contains("Lookup", StringComparison.OrdinalIgnoreCase) || action.Contains("Autocomplete", StringComparison.OrdinalIgnoreCase) || action.Contains("Options", StringComparison.OrdinalIgnoreCase);
        var read = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) ||
            HttpMethods.IsPost(context.Request.Method) && (action.Contains("Search", StringComparison.OrdinalIgnoreCase) || action.Contains("Filter", StringComparison.OrdinalIgnoreCase) || lookup);
        var entry = new UsabilityEvent
        {
            UserId = userId,
            UserName = name,
            EventSource = api ? "Api" : "Page",
            EventKind = UsabilityTrackingActionFilter.IsExport(action) ? "Export" : api && lookup ? "ApiLookup" : !read ? "Mutation" : api ? "ApiView" : "PageView",
            Module = context.Request.RouteValues["area"]?.ToString() ?? descriptor.ControllerName,
            ViewName = descriptor.ControllerName,
            SubView = action.Equals("Index", StringComparison.OrdinalIgnoreCase) ? null : action,
            Route = context.Request.Path.Value ?? "",
            HandlerOrAction = action,
            HttpMethod = context.Request.Method
        };
        context.Items[typeof(UsabilityEvent)] = entry;
        var stopwatch = Stopwatch.StartNew();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            entry.StatusCode = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            entry.Succeeded = entry.StatusCode < 400;
            entry.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
            logger.Log(entry);
        }
    }
}