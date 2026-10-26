using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.Controllers;

public class SuppliersController(IUnitOfWork unitOfWork) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewSupplier)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var suppliers = await unitOfWork.Repository<Supplier>().GetAllAsync(cancellationToken);
        var banks = await unitOfWork.Repository<Bank>().GetAllAsync(cancellationToken);
        ViewData["BankNames"] = banks.ToDictionary(bank => bank.Id, bank => bank.Name);
        return View(suppliers.OrderBy(supplier => supplier.Name).ToList());
    }

    [RequireAction(AuthorizationActionCodes.CreateSupplier)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await PopulateBanksAsync(null, cancellationToken);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateSupplier)]
    public async Task<IActionResult> Create(
        [Bind("Name,Address,RFC,BankId,BankAccount")] Supplier supplier,
        CancellationToken cancellationToken)
    {
        await ValidateBankAsync(supplier.BankId, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateBanksAsync(supplier.BankId, cancellationToken);
            return View(supplier);
        }

        await unitOfWork.Repository<Supplier>().AddAsync(supplier, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [RequireAction(AuthorizationActionCodes.EditSupplier)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var supplier = await unitOfWork.Repository<Supplier>().GetByIdAsync(id, cancellationToken);
        if (supplier is null)
            return NotFound();
        await PopulateBanksAsync(supplier.BankId, cancellationToken);
        return View(supplier);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditSupplier)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name,Address,RFC,BankId,BankAccount")] Supplier input,
        CancellationToken cancellationToken)
    {
        await ValidateBankAsync(input.BankId, cancellationToken);
        if (!ModelState.IsValid)
        {
            input.Id = id;
            await PopulateBanksAsync(input.BankId, cancellationToken);
            return View(input);
        }

        var supplier = await unitOfWork.Repository<Supplier>().GetByIdAsync(id, cancellationToken);
        if (supplier is null)
            return NotFound();

        supplier.Name = input.Name;
        supplier.Address = input.Address;
        supplier.RFC = input.RFC;
        supplier.BankId = input.BankId;
        supplier.BankAccount = input.BankAccount;
        unitOfWork.Repository<Supplier>().Update(supplier);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteSupplier)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var supplier = await unitOfWork.Repository<Supplier>().GetByIdAsync(id, cancellationToken);
        if (supplier is null)
            return NotFound();

        supplier.IsDeleted = true;
        unitOfWork.Repository<Supplier>().Update(supplier);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateBanksAsync(int? selectedId, CancellationToken cancellationToken)
    {
        var banks = await unitOfWork.Repository<Bank>().GetAllAsync(cancellationToken);
        ViewData["Banks"] = new SelectList(banks.OrderBy(bank => bank.Name), "Id", "Name", selectedId);
    }

    private async Task ValidateBankAsync(int bankId, CancellationToken cancellationToken)
    {
        if (bankId <= 0)
            return;
        var bank = await unitOfWork.Repository<Bank>().GetByIdAsync(bankId, cancellationToken);
        if (bank is null)
            ModelState.AddModelError(nameof(Supplier.BankId), "Select a valid bank.");
    }
}
