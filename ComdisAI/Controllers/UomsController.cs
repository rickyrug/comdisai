using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class UomsController(IUnitOfWork unitOfWork, AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var uoms = await unitOfWork.Repository<Uom>().GetAllAsync(cancellationToken);
        return View(uoms.OrderBy(uom => uom.Name).ToList());
    }

    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name")] Uom uom,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidateName(uom);
        if (!ModelState.IsValid)
            return View(uom);

        if (await NameIsReservedAsync(uom.Name, null, cancellationToken))
        {
            ModelState.AddModelError(nameof(Uom.Name), "A UOM with this name already exists.");
            return View(uom);
        }

        await unitOfWork.Repository<Uom>().AddAsync(uom, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(Uom.Name), "A UOM with this name already exists.");
            return View(uom);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var uom = await unitOfWork.Repository<Uom>().GetByIdAsync(id, cancellationToken);
        return uom is null ? NotFound() : View(uom);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name")] Uom input,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidateName(input);
        if (!ModelState.IsValid)
        {
            input.Id = id;
            return View(input);
        }

        var uom = await unitOfWork.Repository<Uom>().GetByIdAsync(id, cancellationToken);
        if (uom is null)
            return NotFound();

        if (await NameIsReservedAsync(input.Name, id, cancellationToken))
        {
            input.Id = id;
            ModelState.AddModelError(nameof(Uom.Name), "A UOM with this name already exists.");
            return View(input);
        }

        uom.Name = input.Name;
        unitOfWork.Repository<Uom>().Update(uom);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            input.Id = id;
            ModelState.AddModelError(nameof(Uom.Name), "A UOM with this name already exists.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var uom = await unitOfWork.Repository<Uom>().GetByIdAsync(id, cancellationToken);
        if (uom is null)
            return NotFound();

        uom.IsDeleted = true;
        unitOfWork.Repository<Uom>().Update(uom);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private void NormalizeAndValidateName(Uom uom)
    {
        uom.Name = uom.Name?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(Uom.Name));
        TryValidateModel(uom);
    }

    private Task<bool> NameIsReservedAsync(
        string name,
        int? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.Uoms
            .IgnoreQueryFilters()
            .AnyAsync(
                uom => EF.Property<string>(uom, "NormalizedName") == name.Trim().ToUpperInvariant()
                    && (!exceptId.HasValue || uom.Id != exceptId.Value),
                cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
