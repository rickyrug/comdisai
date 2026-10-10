using System.Diagnostics;
using ComdisAI.Authorization;
using Microsoft.AspNetCore.Mvc;
using ComdisAI.Models;

namespace ComdisAI.Controllers;

public class HomeController : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewHome)]
    public IActionResult Index()
    {
        return View();
    }

    [RequireAction(AuthorizationActionCodes.ViewPrivacy)]
    public IActionResult Privacy()
    {
        return View();
    }

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
