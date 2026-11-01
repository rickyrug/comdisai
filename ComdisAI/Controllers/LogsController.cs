using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Services;
using ComdisAI.ViewModels.Logs;
using Microsoft.AspNetCore.Mvc;

namespace ComdisAI.Controllers;

public sealed class LogsController(LogViewerService logViewerService) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewLogs)]
    public IActionResult Index(LogViewerViewModel query)
    {
        var model = logViewerService.GetPage(query);
        return View(model);
    }
}
