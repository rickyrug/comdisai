using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class BanksController(IUnitOfWork unitOfWork, AppDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var banks = await unitOfWork.Repository<Bank>().GetAllAsync(cancellationToken);
        return View(banks.OrderBy(bank => bank.Name).ToList());
    }

    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name")] Bank bank,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidateName(bank);
        if (!ModelState.IsValid)
            return View(bank);

        if (await NameIsReservedAsync(bank.Name, null, cancellationToken))
        {
            ModelState.AddModelError(nameof(Bank.Name), "A bank with this name already exists.");
            return View(bank);
        }

        await unitOfWork.Repository<Bank>().AddAsync(bank, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(Bank.Name), "A bank with this name already exists.");
            return View(bank);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var bank = await unitOfWork.Repository<Bank>().GetByIdAsync(id, cancellationToken);
        return bank is null ? NotFound() : View(bank);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name")] Bank input,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidateName(input);
        if (!ModelState.IsValid)
        {
            input.Id = id;
            return View(input);
        }

        var bank = await unitOfWork.Repository<Bank>().GetByIdAsync(id, cancellationToken);
        if (bank is null)
            return NotFound();

        if (await NameIsReservedAsync(input.Name, id, cancellationToken))
        {
            input.Id = id;
            ModelState.AddModelError(nameof(Bank.Name), "A bank with this name already exists.");
            return View(input);
        }

        bank.Name = input.Name;
        unitOfWork.Repository<Bank>().Update(bank);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            input.Id = id;
            ModelState.AddModelError(nameof(Bank.Name), "A bank with this name already exists.");
            return View(input);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var bank = await unitOfWork.Repository<Bank>().GetByIdAsync(id, cancellationToken);
        if (bank is null)
            return NotFound();

        bank.IsDeleted = true;
        unitOfWork.Repository<Bank>().Update(bank);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private void NormalizeAndValidateName(Bank bank)
    {
        bank.Name = bank.Name?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(Bank.Name));
        TryValidateModel(bank);
    }

    private Task<bool> NameIsReservedAsync(
        string name,
        int? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.Banks
            .IgnoreQueryFilters()
            .AnyAsync(
                bank => EF.Property<string>(bank, "NormalizedName") == name.Trim().ToUpperInvariant()
                    && (!exceptId.HasValue || bank.Id != exceptId.Value),
                cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
