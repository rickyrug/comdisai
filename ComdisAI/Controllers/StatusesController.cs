using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class StatusesController(IUnitOfWork unitOfWork, AppDbContext dbContext) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewStatus)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var statuses = await unitOfWork.Repository<Status>().GetAllAsync(cancellationToken);
        return View(statuses.OrderBy(status => status.Entity).ThenBy(status => status.Name).ToList());
    }

    [RequireAction(AuthorizationActionCodes.CreateStatus)]
    public IActionResult Create() =>
        View(new Status { Name = string.Empty, Entity = string.Empty });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateStatus)]
    public async Task<IActionResult> Create(
        [Bind("Name,Entity")] Status status,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidate(status);
        if (!ModelState.IsValid)
            return View(status);

        await AddDuplicateErrorAsync(status, null, cancellationToken);
        if (!ModelState.IsValid)
            return View(status);

        await unitOfWork.Repository<Status>().AddAsync(status, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await AddDuplicateErrorAsync(status, null, cancellationToken);
            if (!ModelState.IsValid)
                return View(status);

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [RequireAction(AuthorizationActionCodes.EditStatus)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var status = await unitOfWork.Repository<Status>().GetByIdAsync(id, cancellationToken);
        return status is null ? NotFound() : View(status);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditStatus)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name,Entity")] Status input,
        CancellationToken cancellationToken)
    {
        input.Id = id;
        NormalizeAndValidate(input);
        if (!ModelState.IsValid)
            return View(input);

        var status = await unitOfWork.Repository<Status>().GetByIdAsync(id, cancellationToken);
        if (status is null)
            return NotFound();

        await AddDuplicateErrorAsync(input, id, cancellationToken);
        if (!ModelState.IsValid)
            return View(input);

        status.Name = input.Name;
        status.Entity = input.Entity;
        unitOfWork.Repository<Status>().Update(status);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await AddDuplicateErrorAsync(input, id, cancellationToken);
            if (!ModelState.IsValid)
                return View(input);

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteStatus)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var status = await unitOfWork.Repository<Status>().GetByIdAsync(id, cancellationToken);
        if (status is null)
            return NotFound();

        status.IsDeleted = true;
        unitOfWork.Repository<Status>().Update(status);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private void NormalizeAndValidate(Status status)
    {
        status.Name = status.Name?.Trim() ?? string.Empty;
        status.Entity = status.Entity?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(Status.Name));
        ModelState.Remove(nameof(Status.Entity));
        TryValidateModel(status);
    }

    private async Task AddDuplicateErrorAsync(
        Status status,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        var normalizedEntity = status.Entity.Trim().ToUpperInvariant();
        var normalizedName = status.Name.Trim().ToUpperInvariant();
        if (await dbContext.Statuses
                .IgnoreQueryFilters()
                .AnyAsync(
                    candidate =>
                        EF.Property<string>(candidate, "NormalizedEntity") == normalizedEntity &&
                        EF.Property<string>(candidate, "NormalizedName") == normalizedName &&
                        (!exceptId.HasValue || candidate.Id != exceptId.Value),
                    cancellationToken))
        {
            ModelState.AddModelError(
                nameof(Status.Name),
                "A status with this name already exists for the selected entity.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
