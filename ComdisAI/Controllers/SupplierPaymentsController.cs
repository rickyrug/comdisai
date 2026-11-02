using System.Globalization;
using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.Services;
using ComdisAI.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class SupplierPaymentsController(
    IUnitOfWork unitOfWork,
    AppDbContext dbContext,
    IActionPermissionService permissionService,
    SupplierPaymentService paymentService) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewSupplierPayment)]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        var query = dbContext.SupplierPayments
            .AsNoTracking()
            .Include(payment => payment.Supplier)
            .Include(payment => payment.Allocations)
            .AsQueryable();
        var term = q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(payment =>
                payment.Supplier.Name.Contains(term) ||
                (payment.Reference != null && payment.Reference.Contains(term)));
        }

        var payments = await query
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.Id)
            .ToListAsync(cancellationToken);
        ViewData["Query"] = term;
        await SetPermissionsAsync(cancellationToken);
        return View(payments);
    }

    [RequireAction(AuthorizationActionCodes.ViewSupplierPayment)]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var payment = await LoadAsync(id, cancellationToken);
        if (payment is null)
            return NotFound();

        ViewData["OpenOrders"] = await paymentService.GetOpenOrdersAsync(payment.SupplierId, cancellationToken);
        await SetPermissionsAsync(cancellationToken);
        return View(payment);
    }

    [HttpGet]
    [RequireAction(AuthorizationActionCodes.CreateSupplierPayment)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new SupplierPaymentInputViewModel();
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateSupplierPayment)]
    public async Task<IActionResult> Create(
        SupplierPaymentInputViewModel input,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(input, null, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var payment = new SupplierPayment();
        Apply(payment, input);
        await unitOfWork.Repository<SupplierPayment>().AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Details), new { id = payment.Id });
    }

    [HttpGet]
    [RequireAction(AuthorizationActionCodes.EditSupplierPayment)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var payment = await unitOfWork.Repository<SupplierPayment>().GetByIdAsync(id, cancellationToken);
        if (payment is null)
            return NotFound();

        var model = new SupplierPaymentInputViewModel
        {
            Id = payment.Id,
            SupplierId = payment.SupplierId,
            PaymentDate = payment.PaymentDate,
            Amount = payment.Amount,
            Method = payment.Method,
            Reference = payment.Reference,
            BankId = payment.BankId,
            Notes = payment.Notes
        };
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditSupplierPayment)]
    public async Task<IActionResult> Edit(
        int id,
        SupplierPaymentInputViewModel input,
        CancellationToken cancellationToken)
    {
        input.Id = id;
        var payment = await dbContext.SupplierPayments
            .Include(row => row.Allocations)
            .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (payment is null)
            return NotFound();

        await ValidateAsync(input, payment, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(input, cancellationToken);
            return View(input);
        }

        Apply(payment, input);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteSupplierPayment)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var payment = await unitOfWork.Repository<SupplierPayment>().GetByIdAsync(id, cancellationToken);
        if (payment is null)
            return NotFound();

        payment.IsDeleted = true;
        unitOfWork.Repository<SupplierPayment>().Update(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.AllocateSupplierPayment)]
    public async Task<IActionResult> Allocate(
        int id,
        int purchaseOrderId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var error = await paymentService.AllocateAsync(id, purchaseOrderId, amount, cancellationToken);
        if (error is null)
            TempData["Success"] = "Payment allocated.";
        else
            TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.AllocateSupplierPayment)]
    public async Task<IActionResult> RemoveAllocation(
        int id,
        int allocationId,
        CancellationToken cancellationToken)
    {
        var allocation = await dbContext.SupplierPaymentAllocations
            .SingleOrDefaultAsync(row => row.Id == allocationId && row.SupplierPaymentId == id, cancellationToken);
        if (allocation is null)
            return NotFound();

        dbContext.SupplierPaymentAllocations.Remove(allocation);
        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Allocation removed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private Task<SupplierPayment?> LoadAsync(int id, CancellationToken cancellationToken) =>
        dbContext.SupplierPayments
            .AsNoTracking()
            .Include(payment => payment.Supplier)
            .Include(payment => payment.Bank)
            .Include(payment => payment.Allocations)
                .ThenInclude(allocation => allocation.PurchaseOrderHeader)
            .SingleOrDefaultAsync(payment => payment.Id == id, cancellationToken);

    private async Task SetPermissionsAsync(CancellationToken cancellationToken)
    {
        ViewData["CanCreate"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.CreateSupplierPayment, cancellationToken);
        ViewData["CanEdit"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.EditSupplierPayment, cancellationToken);
        ViewData["CanDelete"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.DeleteSupplierPayment, cancellationToken);
        ViewData["CanAllocate"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.AllocateSupplierPayment, cancellationToken);
    }

    private async Task ValidateAsync(
        SupplierPaymentInputViewModel input,
        SupplierPayment? existing,
        CancellationToken cancellationToken)
    {
        if (input.SupplierId is { } supplierId &&
            !await dbContext.Suppliers.AnyAsync(supplier => supplier.Id == supplierId, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.SupplierId), "Select an active supplier.");
        }
        if (input.BankId is { } bankId &&
            !await dbContext.Banks.AnyAsync(bank => bank.Id == bankId, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.BankId), "Select an active bank.");
        }
        if (existing is not null)
        {
            if (input.SupplierId != existing.SupplierId && existing.Allocations.Count > 0)
            {
                ModelState.AddModelError(
                    nameof(input.SupplierId), "Remove the allocations before changing the supplier.");
            }
            if (input.Amount is { } amount &&
                SupplierPaymentService.ValidateAmountChange(existing, amount) is { } error)
            {
                ModelState.AddModelError(nameof(input.Amount), error);
            }
        }
    }

    private static void Apply(SupplierPayment payment, SupplierPaymentInputViewModel input)
    {
        payment.SupplierId = input.SupplierId!.Value;
        payment.PaymentDate = input.PaymentDate!.Value.Date;
        payment.Amount = decimal.Round(input.Amount!.Value, 4, MidpointRounding.AwayFromZero);
        payment.Method = input.Method!.Value;
        payment.Reference = string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim();
        payment.BankId = input.BankId;
        payment.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
    }

    private async Task PopulateOptionsAsync(
        SupplierPaymentInputViewModel model,
        CancellationToken cancellationToken)
    {
        var suppliers = await dbContext.Suppliers.AsNoTracking()
            .OrderBy(supplier => supplier.Name).ToListAsync(cancellationToken);
        var banks = await dbContext.Banks.AsNoTracking()
            .OrderBy(bank => bank.Name).ToListAsync(cancellationToken);
        ViewData["Suppliers"] = new SelectList(suppliers, nameof(Supplier.Id), nameof(Supplier.Name), model.SupplierId);
        ViewData["Banks"] = new SelectList(banks, nameof(Bank.Id), nameof(Bank.Name), model.BankId);
        ViewData["Methods"] = Enum.GetValues<PaymentMethod>()
            .Select(method => new SelectListItem(
                method.ToString(),
                method.ToString(),
                model.Method == method))
            .ToList();
    }
}
