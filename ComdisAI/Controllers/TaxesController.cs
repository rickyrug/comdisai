using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class TaxesController(IUnitOfWork unitOfWork, AppDbContext dbContext) : Controller
{
    private const decimal MaximumTaxValue = 99_999_999_999_999.9999m;

    [RequireAction(AuthorizationActionCodes.ViewTax)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var taxes = await unitOfWork.Repository<Tax>().GetAllAsync(cancellationToken);
        return View(taxes.OrderBy(tax => tax.Name).ToList());
    }

    [RequireAction(AuthorizationActionCodes.CreateTax)]
    public IActionResult Create() => View(new Tax { Name = string.Empty });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateTax)]
    public async Task<IActionResult> Create(
        [Bind("Name,Value")] Tax tax,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidate(tax);
        if (!ModelState.IsValid)
            return View(tax);

        if (await NameIsReservedAsync(tax.Name, null, cancellationToken))
        {
            ModelState.AddModelError(nameof(Tax.Name), "A tax with this name already exists.");
            return View(tax);
        }

        await unitOfWork.Repository<Tax>().AddAsync(tax, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(Tax.Name), "A tax with this name already exists.");
            return View(tax);
        }

        return RedirectToAction(nameof(Index));
    }

    [RequireAction(AuthorizationActionCodes.EditTax)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var tax = await unitOfWork.Repository<Tax>().GetByIdAsync(id, cancellationToken);
        return tax is null ? NotFound() : View(tax);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditTax)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name,Value")] Tax input,
        CancellationToken cancellationToken)
    {
        input.Id = id;
        NormalizeAndValidate(input);
        if (!ModelState.IsValid)
            return View(input);

        var tax = await unitOfWork.Repository<Tax>().GetByIdAsync(id, cancellationToken);
        if (tax is null)
            return NotFound();

        if (await NameIsReservedAsync(input.Name, id, cancellationToken))
        {
            ModelState.AddModelError(nameof(Tax.Name), "A tax with this name already exists.");
            return View(input);
        }

        tax.Name = input.Name;
        tax.Value = input.Value;
        unitOfWork.Repository<Tax>().Update(tax);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(Tax.Name), "A tax with this name already exists.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteTax)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var tax = await unitOfWork.Repository<Tax>().GetByIdAsync(id, cancellationToken);
        if (tax is null)
            return NotFound();

        tax.IsDeleted = true;
        unitOfWork.Repository<Tax>().Update(tax);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private void NormalizeAndValidate(Tax tax)
    {
        tax.Name = tax.Name?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(Tax.Name));
        TryValidateModel(tax);

        if (tax.Value < -MaximumTaxValue ||
            tax.Value > MaximumTaxValue ||
            decimal.Round(tax.Value, 4) != tax.Value)
        {
            ModelState.AddModelError(nameof(Tax.Value), "Tax value must fit decimal(18,4).");
        }
    }

    private Task<bool> NameIsReservedAsync(
        string name,
        int? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.Taxes
            .IgnoreQueryFilters()
            .AnyAsync(
                tax => EF.Property<string>(tax, "NormalizedName") == name.Trim().ToUpperInvariant()
                    && (!exceptId.HasValue || tax.Id != exceptId.Value),
                cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
