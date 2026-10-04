using System.Globalization;
using System.Security.Claims;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class AuthenticationController(
    IUnitOfWork unitOfWork,
    IPasswordHasher<User> passwordHasher) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel input,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
            return View(input);

        var normalizedEmail = ComdisAI.Models.User.NormalizeEmail(input.Email);
        var user = await unitOfWork.Repository<User>().GetFirstOrDefaultAsync(
            candidate => EF.Property<string>(candidate, "NormalizedEmail") == normalizedEmail,
            cancellationToken);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(input);
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, input.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View(input);
        }

        user.LastLoginDateUtc = DateTime.UtcNow;
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, input.Password);

        unitOfWork.Repository<User>().Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        Claim[] claims =
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, $"{user.Name} {user.Surname}".Trim()),
            new Claim(ClaimTypes.Email, user.Email)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);

        return Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl!)
            : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
