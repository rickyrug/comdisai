using System.Globalization;
using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Services;
using ComdisAI.ViewModels.Logs;
using Microsoft.AspNetCore.Mvc;

namespace ComdisAI.Controllers;

public sealed class AdminController(IUsabilityAnalyticsService usability, IAppLogService logs) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewLogs)]
    public IActionResult Usability(UsabilityDashboard query)
    {
        query.From = ParseUtc("From");
        query.To = ParseUtc("To");
        return View(usability.GetDashboard(query));
    }

    [RequireAction(AuthorizationActionCodes.ViewLogs)]
    public IActionResult AppLogs(AppLogDashboard query)
    {
        query.From = ParseUtc("From");
        query.To = ParseUtc("To");
        return View(logs.GetDashboard(query));
    }

    private DateTimeOffset? ParseUtc(string name) =>
        DateTimeOffset.TryParse(Request.Query[name], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : null;
}