using System.Globalization;
using System.Security.Claims;
using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.Services;
using ComdisAI.ViewModels.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class AuthenticationController(
    IUnitOfWork unitOfWork,
    IPasswordHasher<User> passwordHasher,
    IActionPermissionService permissionService) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [AllowAnonymous]
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

        var roles = await permissionService.GetActiveRolesAsync(user.Id, cancellationToken);
        if (roles.Count == 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ModelState.AddModelError(string.Empty, "This account has no active role. Contact an administrator.");
            return View(input);
        }

        if (roles.Count > 1)
        {
            return RedirectToAction(nameof(SelectRole), new { returnUrl });
        }

        await SignInWithActiveRoleAsync(user, roles[0].Id);
        return Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl!)
            : RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> SelectRole(
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Challenge();

        var roles = await permissionService.GetActiveRolesAsync(userId.Value, cancellationToken);
        if (roles.Count == 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        if (roles.Count == 1)
        {
            var user = await unitOfWork.Repository<User>().GetByIdAsync(userId.Value, cancellationToken);
            if (user is null)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction(nameof(Login));
            }

            await SignInWithActiveRoleAsync(user, roles[0].Id);
            return Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl!)
                : RedirectToAction("Index", "Home");
        }

        return View(new SelectRoleViewModel
        {
            ReturnUrl = returnUrl,
            Roles = roles
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectRole(
        SelectRoleViewModel input,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Challenge();

        var roles = await permissionService.GetActiveRolesAsync(userId.Value, cancellationToken);
        input.Roles = roles;
        if (!ModelState.IsValid ||
            input.SelectedRoleId is not int selectedRoleId ||
            roles.All(role => role.Id != selectedRoleId))
        {
            ModelState.AddModelError(nameof(input.SelectedRoleId), "Select one of your active roles.");
            return View(input);
        }

        var user = await unitOfWork.Repository<User>().GetByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        await SignInWithActiveRoleAsync(user, selectedRoleId);
        return Url.IsLocalUrl(input.ReturnUrl)
            ? LocalRedirect(input.ReturnUrl!)
            : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private int? GetCurrentUserId() =>
        int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var userId)
            ? userId
            : null;

    private async Task SignInWithActiveRoleAsync(User user, int roleId)
    {
        Claim[] claims =
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, $"{user.Name} {user.Surname}".Trim()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(AuthorizationClaims.ActiveRoleId, roleId.ToString(CultureInfo.InvariantCulture))
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal);
    }
}
