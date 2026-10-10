using ComdisAI.Data;
using ComdisAI.Authorization;
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
    IPasswordHasher<User> passwordHasher,
    AppDbContext dbContext) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewUser)]
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

    [RequireAction(AuthorizationActionCodes.AssignUserRoles)]
    public async Task<IActionResult> ManageRoles(int? id, CancellationToken cancellationToken)
    {
        var users = await unitOfWork.Repository<User>().GetAllAsync(cancellationToken);
        var selectedUserId = id ?? users.OrderBy(user => user.Email).FirstOrDefault()?.Id;
        if (selectedUserId.HasValue && users.All(user => user.Id != selectedUserId.Value))
            return NotFound();

        return View(await BuildManageRolesViewModelAsync(selectedUserId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.AssignUserRoles)]
    public async Task<IActionResult> ManageRoles(
        ManageUserRolesViewModel input,
        CancellationToken cancellationToken)
    {
        if (!input.UserId.HasValue)
            return BadRequest();

        var user = await unitOfWork.Repository<User>().GetByIdAsync(input.UserId.Value, cancellationToken);
        if (user is null)
            return NotFound();

        var selectedRoleIds = input.SelectedRoleIds.Distinct().ToArray();
        if (selectedRoleIds.Length != input.SelectedRoleIds.Count)
        {
            ModelState.AddModelError(nameof(input.SelectedRoleIds), "A role was selected more than once.");
            return View(await BuildManageRolesViewModelAsync(
                input.UserId,
                cancellationToken,
                input.SelectedRoleIds));
        }

        var availableRoleIds = await dbContext.Roles
            .Select(role => role.Id)
            .ToListAsync(cancellationToken);
        if (selectedRoleIds.Any(roleId => !availableRoleIds.Contains(roleId)))
        {
            ModelState.AddModelError(
                nameof(input.SelectedRoleIds),
                "One or more selected roles are no longer available.");
            return View(await BuildManageRolesViewModelAsync(
                input.UserId,
                cancellationToken,
                input.SelectedRoleIds));
        }

        var assignments = await dbContext.UserRoles
            .IgnoreQueryFilters()
            .Where(userRole => userRole.UserId == user.Id)
            .ToListAsync(cancellationToken);
        var activeAssignments = assignments
            .Where(userRole => availableRoleIds.Contains(userRole.RoleId))
            .ToList();
        var assignedRoleIds = activeAssignments.Select(userRole => userRole.RoleId).ToHashSet();
        var assignmentsToRemove = activeAssignments
            .Where(userRole => !selectedRoleIds.Contains(userRole.RoleId))
            .ToArray();
        var roleIdsToAdd = selectedRoleIds
            .Where(roleId => !assignedRoleIds.Contains(roleId))
            .ToArray();

        dbContext.UserRoles.RemoveRange(assignmentsToRemove);
        await dbContext.UserRoles.AddRangeAsync(
            roleIdsToAdd.Select(roleId => new UserRole { UserId = user.Id, RoleId = roleId }),
            cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUserRoleUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(
                string.Empty,
                "The role assignments changed while you were saving. Review the current assignments and try again.");
            return View(await BuildManageRolesViewModelAsync(
                input.UserId,
                cancellationToken,
                input.SelectedRoleIds));
        }

        return RedirectToAction(nameof(ManageRoles), new { id = user.Id });
    }

    [RequireAction(AuthorizationActionCodes.CreateUser)]
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateUser)]
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

    [RequireAction(AuthorizationActionCodes.EditUser)]
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
    [RequireAction(AuthorizationActionCodes.EditUser)]
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
    [RequireAction(AuthorizationActionCodes.DeleteUser)]
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
    [RequireAction(AuthorizationActionCodes.ResetUserPassword)]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.Repository<User>().GetByIdAsync(id, cancellationToken);
        return user is null
            ? NotFound()
            : View(new ResetUserPasswordViewModel { Id = user.Id, Email = user.Email });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.ResetUserPassword)]
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

    private async Task<ManageUserRolesViewModel> BuildManageRolesViewModelAsync(
        int? userId,
        CancellationToken cancellationToken,
        IEnumerable<int>? selectedRoleIds = null)
    {
        var users = (await unitOfWork.Repository<User>().GetAllAsync(cancellationToken))
            .OrderBy(user => user.Email)
            .Select(user => new UserListItemViewModel
            {
                Id = user.Id,
                Name = user.Name,
                Surname = user.Surname,
                Email = user.Email,
                LastLoginDateUtc = user.LastLoginDateUtc
            })
            .ToList();
        var roles = await dbContext.Roles
            .AsNoTracking()
            .OrderBy(role => role.Code)
            .Select(role => new UserRoleOptionViewModel
            {
                Id = role.Id,
                Code = role.Code,
                Description = role.Description
            })
            .ToListAsync(cancellationToken);
        var assignedRoleIds = userId.HasValue
            ? await dbContext.UserRoles
                .IgnoreQueryFilters()
                .Where(userRole => userRole.UserId == userId.Value)
                .Select(userRole => userRole.RoleId)
                .ToListAsync(cancellationToken)
            : [];
        var availableRoleIds = roles.Select(role => role.Id).ToHashSet();

        return new ManageUserRolesViewModel
        {
            UserId = userId,
            SelectedRoleIds = selectedRoleIds?.ToList()
                ?? assignedRoleIds.Where(availableRoleIds.Contains).ToList(),
            Users = users,
            Roles = roles
        };
    }

    private static bool IsEmailUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19
        } sqliteException &&
        sqliteException.Message.Contains("IX_Users_NormalizedEmail", StringComparison.Ordinal);

    private static bool IsUserRoleUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
