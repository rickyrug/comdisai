using System.Globalization;
using ComdisAI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ComdisAI.Filters;

public sealed class UsabilityTrackingActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> IdentifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "plantId", "siteId", "tenantId", "customerId", "supplierId", "productId", "orderId"
    };
    private static readonly HashSet<string> ViewNames = new(StringComparer.OrdinalIgnoreCase) { "tab", "view", "mode" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Items.TryGetValue(typeof(UsabilityEvent), out var value) && value is UsabilityEvent entry)
        {
            foreach (var argument in context.ActionArguments)
                AddContext(entry, argument.Key, argument.Value);
            foreach (var route in context.RouteData.Values)
                AddContext(entry, route.Key, route.Value);
            foreach (var query in context.HttpContext.Request.Query)
                AddContext(entry, query.Key, query.Value.ToString());

            var result = await next();
            if (result.Result is FileResult || IsExport(entry.HandlerOrAction))
                entry.EventKind = "Export";
            if (result.Result is ViewResult view && !string.IsNullOrWhiteSpace(view.ViewName))
                entry.SubView = view.ViewName;
            return;
        }
        await next();
    }

    internal static bool IsExport(string action) =>
        action.Contains("Export", StringComparison.OrdinalIgnoreCase) ||
        action.Contains("Download", StringComparison.OrdinalIgnoreCase);

    private static void AddContext(UsabilityEvent entry, string name, object? value)
    {
        if (value is null)
            return;
        if (IdentifierNames.Contains(name))
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (text is { Length: > 0 and <= 64 } && text.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
                entry.DomainIds[name] = text;
            return;
        }
        if (ViewNames.Contains(name) && value is string view && view.Length is > 0 and <= 64 && view.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
        {
            entry.SubView = view;
            return;
        }
        if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value is CancellationToken)
            return;
        foreach (var property in value.GetType().GetProperties())
        {
            if ((IdentifierNames.Contains(property.Name) || ViewNames.Contains(property.Name)) && property.CanRead && property.GetIndexParameters().Length == 0)
                AddContext(entry, property.Name, property.GetValue(value));
        }
    }
}