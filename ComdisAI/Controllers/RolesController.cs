using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.Roles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class RolesController(IUnitOfWork unitOfWork, AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var roles = await unitOfWork.Repository<Role>().GetAllAsync(cancellationToken);
        return View(roles.OrderBy(role => role.Code).ToList());
    }

    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Code,Description")] Role input,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidate(input);
        if (!ModelState.IsValid)
            return View(input);

        if (await CodeIsReservedAsync(input.Code, null, cancellationToken))
        {
            ModelState.AddModelError(nameof(Role.Code), "A role with this code already exists.");
            return View(input);
        }

        await unitOfWork.Repository<Role>().AddAsync(input, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(Role.Code), "A role with this code already exists.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var role = await unitOfWork.Repository<Role>().GetByIdAsync(id, cancellationToken);
        return role is null ? NotFound() : View(role);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Code,Description")] Role input,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidate(input);
        if (!ModelState.IsValid)
        {
            input.Id = id;
            return View(input);
        }

        var role = await unitOfWork.Repository<Role>().GetByIdAsync(id, cancellationToken);
        if (role is null)
            return NotFound();

        if (await CodeIsReservedAsync(input.Code, id, cancellationToken))
        {
            input.Id = id;
            ModelState.AddModelError(nameof(Role.Code), "A role with this code already exists.");
            return View(input);
        }

        role.Code = input.Code;
        role.Description = input.Description;
        unitOfWork.Repository<Role>().Update(role);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            input.Id = id;
            ModelState.AddModelError(nameof(Role.Code), "A role with this code already exists.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var role = await unitOfWork.Repository<Role>().GetByIdAsync(id, cancellationToken);
        if (role is null)
            return NotFound();

        role.IsDeleted = true;
        unitOfWork.Repository<Role>().Update(role);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ManageActions(int id, CancellationToken cancellationToken)
    {
        if (await unitOfWork.Repository<Role>().GetByIdAsync(id, cancellationToken) is null)
            return NotFound();

        return View(await BuildManageActionsViewModelAsync(id, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddActions(
        int id,
        ManageRoleActionsViewModel input,
        CancellationToken cancellationToken)
    {
        if (id != input.RoleId)
            return BadRequest();

        var role = await unitOfWork.Repository<Role>().GetByIdAsync(id, cancellationToken);
        if (role is null)
            return NotFound();

        var selectedActionIds = input.SelectedActionIds.Distinct().ToArray();
        if (input.SelectedActionIds.Count == 0)
        {
            ModelState.AddModelError(nameof(input.SelectedActionIds), "Select at least one action.");
            return View(nameof(ManageActions), await BuildManageActionsViewModelAsync(
                id,
                cancellationToken,
                input.SelectedActionIds));
        }

        if (selectedActionIds.Length != input.SelectedActionIds.Count)
        {
            ModelState.AddModelError(nameof(input.SelectedActionIds), "An action was selected more than once.");
            return View(nameof(ManageActions), await BuildManageActionsViewModelAsync(
                id,
                cancellationToken,
                input.SelectedActionIds));
        }

        if (!ModelState.IsValid)
            return View(nameof(ManageActions), await BuildManageActionsViewModelAsync(
                id,
                cancellationToken,
                input.SelectedActionIds));

        var availableActionIds = await dbContext.Actions
            .Where(action => selectedActionIds.Contains(action.Id))
            .Select(action => action.Id)
            .ToListAsync(cancellationToken);
        if (availableActionIds.Count != selectedActionIds.Length)
        {
            ModelState.AddModelError(
                nameof(input.SelectedActionIds),
                "One or more selected actions are no longer available.");
            return View(nameof(ManageActions), await BuildManageActionsViewModelAsync(
                id,
                cancellationToken,
                input.SelectedActionIds));
        }

        var alreadyAssignedActionIds = await dbContext.RoleActions
            .IgnoreQueryFilters()
            .Where(roleAction => roleAction.RoleId == id && selectedActionIds.Contains(roleAction.ActionId))
            .Select(roleAction => roleAction.ActionId)
            .ToListAsync(cancellationToken);
        if (alreadyAssignedActionIds.Count > 0)
        {
            ModelState.AddModelError(
                nameof(input.SelectedActionIds),
                "One or more selected actions are already assigned to the role.");
            return View(nameof(ManageActions), await BuildManageActionsViewModelAsync(
                id,
                cancellationToken,
                input.SelectedActionIds));
        }

        await dbContext.RoleActions.AddRangeAsync(
            selectedActionIds.Select(actionId => new RoleAction { RoleId = id, ActionId = actionId }),
            cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(
                nameof(input.SelectedActionIds),
                "One or more selected actions were assigned to the role by another request.");
            return View(nameof(ManageActions), await BuildManageActionsViewModelAsync(
                id,
                cancellationToken,
                input.SelectedActionIds));
        }

        return RedirectToAction(nameof(ManageActions), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAction(
        int id,
        int roleActionId,
        CancellationToken cancellationToken)
    {
        if (await unitOfWork.Repository<Role>().GetByIdAsync(id, cancellationToken) is null)
            return NotFound();

        var roleAction = await dbContext.RoleActions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
            assignment => assignment.Id == roleActionId && assignment.RoleId == id,
            cancellationToken);
        if (roleAction is null)
            return NotFound();

        dbContext.RoleActions.Remove(roleAction);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(ManageActions), new { id });
    }

    private async Task<ManageRoleActionsViewModel> BuildManageActionsViewModelAsync(
        int roleId,
        CancellationToken cancellationToken,
        IEnumerable<int>? selectedActionIds = null)
    {
        var role = await unitOfWork.Repository<Role>().GetByIdAsync(roleId, cancellationToken)
            ?? throw new InvalidOperationException($"Role {roleId} was not found.");
        var assignments = await dbContext.RoleActions
            .IgnoreQueryFilters()
            .Where(roleAction => roleAction.RoleId == roleId)
            .Join(
                dbContext.Actions.IgnoreQueryFilters(),
                roleAction => roleAction.ActionId,
                action => action.Id,
                (roleAction, action) => new RoleActionListItemViewModel
                {
                    RoleActionId = roleAction.Id,
                    ActionId = action.Id,
                    Code = action.Code,
                    Description = action.Description,
                    IsDeleted = action.IsDeleted
                })
            .OrderBy(action => action.Code)
            .ToListAsync(cancellationToken);
        var assignedActionIds = assignments.Select(assignment => assignment.ActionId).ToArray();
        var availableActions = await dbContext.Actions
            .Where(action => !assignedActionIds.Contains(action.Id))
            .OrderBy(action => action.Code)
            .Select(action => new RoleActionOptionViewModel
            {
                Id = action.Id,
                Code = action.Code,
                Description = action.Description
            })
            .ToListAsync(cancellationToken);

        return new ManageRoleActionsViewModel
        {
            RoleId = role.Id,
            RoleCode = role.Code,
            SelectedActionIds = selectedActionIds?.ToList() ?? [],
            AssignedActions = assignments,
            AvailableActions = availableActions
        };
    }

    private void NormalizeAndValidate(Role role)
    {
        role.Code = role.Code?.Trim() ?? string.Empty;
        role.Description = role.Description?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(Role.Code));
        ModelState.Remove(nameof(Role.Description));
        TryValidateModel(role);
    }

    private Task<bool> CodeIsReservedAsync(
        string code,
        int? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.Roles
            .IgnoreQueryFilters()
            .AnyAsync(
                role => EF.Property<string>(role, "NormalizedCode") == code.Trim().ToUpperInvariant()
                    && (!exceptId.HasValue || role.Id != exceptId.Value),
                cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
