using ComdisAI.Data;
using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class ActionsController(IUnitOfWork unitOfWork, AppDbContext dbContext) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewAction)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var actions = await unitOfWork.Repository<Actions>().GetAllAsync(cancellationToken);
        return View(actions.OrderBy(action => action.Code).ToList());
    }

    [RequireAction(AuthorizationActionCodes.CreateAction)]
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateAction)]
    public async Task<IActionResult> Create(
        [Bind("Code,Description")] Actions input,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidate(input);
        if (!ModelState.IsValid)
            return View(input);

        if (await CodeIsReservedAsync(input.Code, null, cancellationToken))
        {
            ModelState.AddModelError(nameof(Actions.Code), "An action with this code already exists.");
            return View(input);
        }

        await unitOfWork.Repository<Actions>().AddAsync(input, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(Actions.Code), "An action with this code already exists.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    [RequireAction(AuthorizationActionCodes.EditAction)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var action = await unitOfWork.Repository<Actions>().GetByIdAsync(id, cancellationToken);
        return action is null ? NotFound() : View(action);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditAction)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Code,Description")] Actions input,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidate(input);
        if (!ModelState.IsValid)
        {
            input.Id = id;
            return View(input);
        }

        var action = await unitOfWork.Repository<Actions>().GetByIdAsync(id, cancellationToken);
        if (action is null)
            return NotFound();

        if (await CodeIsReservedAsync(input.Code, id, cancellationToken))
        {
            input.Id = id;
            ModelState.AddModelError(nameof(Actions.Code), "An action with this code already exists.");
            return View(input);
        }

        action.Code = input.Code;
        action.Description = input.Description;
        unitOfWork.Repository<Actions>().Update(action);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            input.Id = id;
            ModelState.AddModelError(nameof(Actions.Code), "An action with this code already exists.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteAction)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var action = await unitOfWork.Repository<Actions>().GetByIdAsync(id, cancellationToken);
        if (action is null)
            return NotFound();

        action.IsDeleted = true;
        unitOfWork.Repository<Actions>().Update(action);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private void NormalizeAndValidate(Actions action)
    {
        action.Code = action.Code?.Trim() ?? string.Empty;
        action.Description = action.Description?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(Actions.Code));
        ModelState.Remove(nameof(Actions.Description));
        TryValidateModel(action);
    }

    private Task<bool> CodeIsReservedAsync(
        string code,
        int? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.Actions
            .IgnoreQueryFilters()
            .AnyAsync(
                action => EF.Property<string>(action, "NormalizedCode") == code.Trim().ToUpperInvariant()
                    && (!exceptId.HasValue || action.Id != exceptId.Value),
                cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
