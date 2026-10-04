using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class UsersController(
    IUnitOfWork unitOfWork,
    IPasswordHasher<User> passwordHasher) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var users = await unitOfWork.Repository<User>().GetAllAsync(cancellationToken);
        return View(users
            .OrderBy(user => user.Email)
            .Select(user => new UserListItemViewModel
            {
                Id = user.Id,
                Name = user.Name,
                Surname = user.Surname,
                Email = user.Email,
                LastLoginDateUtc = user.LastLoginDateUtc
            })
            .ToList());
    }

    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateUserViewModel input,
        CancellationToken cancellationToken)
    {
        input.Email = (input.Email ?? string.Empty).Trim();
        if (ModelState.IsValid && await EmailExistsAsync(input.Email, null, cancellationToken))
            ModelState.AddModelError(nameof(input.Email), "That email address is already in use.");

        if (!ModelState.IsValid)
            return View(input);

        var user = new User
        {
            Name = input.Name.Trim(),
            Surname = input.Surname.Trim(),
            Email = input.Email,
            PasswordHash = string.Empty
        };
        user.PasswordHash = passwordHasher.HashPassword(user, input.Password);

        await unitOfWork.Repository<User>().AddAsync(user, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEmailUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(input.Email), "That email address is already in use.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        return user is null
            ? NotFound()
            : View(new EditUserViewModel
            {
                Id = user.Id,
                Name = user.Name,
                Surname = user.Surname,
                Email = user.Email
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        EditUserViewModel input,
        CancellationToken cancellationToken)
    {
        if (id != input.Id)
            return BadRequest();

        input.Email = (input.Email ?? string.Empty).Trim();
        if (ModelState.IsValid && await EmailExistsAsync(input.Email, id, cancellationToken))
            ModelState.AddModelError(nameof(input.Email), "That email address is already in use.");

        if (!ModelState.IsValid)
            return View(input);

        var user = await unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        if (user is null)
            return NotFound();

        user.Name = input.Name.Trim();
        user.Surname = input.Surname.Trim();
        user.Email = input.Email;
        unitOfWork.Repository<User>().Update(user);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEmailUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(input.Email), "That email address is already in use.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        if (user is null)
            return NotFound();

        user.IsDeleted = true;
        unitOfWork.Repository<User>().Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        return user is null
            ? NotFound()
            : View(new ResetUserPasswordViewModel { Id = user.Id, Email = user.Email });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(
        int id,
        ResetUserPasswordViewModel input,
        CancellationToken cancellationToken)
    {
        if (id != input.Id)
            return BadRequest();

        if (!ModelState.IsValid)
            return View(input);

        var user = await unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        if (user is null)
            return NotFound();

        user.PasswordHash = passwordHasher.HashPassword(user, input.Password);
        unitOfWork.Repository<User>().Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> EmailExistsAsync(
        string email,
        int? excludingUserId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = ComdisAI.Models.User.NormalizeEmail(email);
        return await unitOfWork.Repository<User>().GetFirstOrDefaultIncludingDeletedAsync(
            user => user.Id != excludingUserId &&
                    EF.Property<string>(user, "NormalizedEmail") == normalizedEmail,
            cancellationToken) is not null;
    }

    private static bool IsEmailUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19
        } sqliteException &&
        sqliteException.Message.Contains("IX_Users_NormalizedEmail", StringComparison.Ordinal);
}
