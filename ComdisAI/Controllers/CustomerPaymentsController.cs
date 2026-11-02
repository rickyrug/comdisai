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

public class CustomerPaymentsController(
    IUnitOfWork unitOfWork,
    AppDbContext dbContext,
    IActionPermissionService permissionService,
    CustomerPaymentService paymentService) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewCustomerPayment)]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        var query = dbContext.CustomerPayments
            .AsNoTracking()
            .Include(payment => payment.Customer)
            .Include(payment => payment.Allocations)
            .AsQueryable();
        var term = q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(payment =>
                payment.Customer.Name.Contains(term) ||
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

    [RequireAction(AuthorizationActionCodes.ViewCustomerPayment)]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var payment = await LoadAsync(id, cancellationToken);
        if (payment is null)
            return NotFound();

        ViewData["OpenOrders"] = await paymentService.GetOpenOrdersAsync(payment.CustomerId, cancellationToken);
        await SetPermissionsAsync(cancellationToken);
        return View(payment);
    }

    [HttpGet]
    [RequireAction(AuthorizationActionCodes.CreateCustomerPayment)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new CustomerPaymentInputViewModel();
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateCustomerPayment)]
    public async Task<IActionResult> Create(
        CustomerPaymentInputViewModel input,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(input, null, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var payment = new CustomerPayment();
        Apply(payment, input);
        await unitOfWork.Repository<CustomerPayment>().AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Details), new { id = payment.Id });
    }

    [HttpGet]
    [RequireAction(AuthorizationActionCodes.EditCustomerPayment)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var payment = await unitOfWork.Repository<CustomerPayment>().GetByIdAsync(id, cancellationToken);
        if (payment is null)
            return NotFound();

        var model = new CustomerPaymentInputViewModel
        {
            Id = payment.Id,
            CustomerId = payment.CustomerId,
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
    [RequireAction(AuthorizationActionCodes.EditCustomerPayment)]
    public async Task<IActionResult> Edit(
        int id,
        CustomerPaymentInputViewModel input,
        CancellationToken cancellationToken)
    {
        input.Id = id;
        var payment = await dbContext.CustomerPayments
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
    [RequireAction(AuthorizationActionCodes.DeleteCustomerPayment)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var payment = await unitOfWork.Repository<CustomerPayment>().GetByIdAsync(id, cancellationToken);
        if (payment is null)
            return NotFound();

        payment.IsDeleted = true;
        unitOfWork.Repository<CustomerPayment>().Update(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.AllocateCustomerPayment)]
    public async Task<IActionResult> Allocate(
        int id,
        int salesOrderId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var error = await paymentService.AllocateAsync(id, salesOrderId, amount, cancellationToken);
        if (error is null)
            TempData["Success"] = "Payment allocated.";
        else
            TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.AllocateCustomerPayment)]
    public async Task<IActionResult> RemoveAllocation(
        int id,
        int allocationId,
        CancellationToken cancellationToken)
    {
        var allocation = await dbContext.CustomerPaymentAllocations
            .SingleOrDefaultAsync(row => row.Id == allocationId && row.CustomerPaymentId == id, cancellationToken);
        if (allocation is null)
            return NotFound();

        dbContext.CustomerPaymentAllocations.Remove(allocation);
        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Allocation removed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private Task<CustomerPayment?> LoadAsync(int id, CancellationToken cancellationToken) =>
        dbContext.CustomerPayments
            .AsNoTracking()
            .Include(payment => payment.Customer)
            .Include(payment => payment.Bank)
            .Include(payment => payment.Allocations)
                .ThenInclude(allocation => allocation.SalesOrderHeader)
            .SingleOrDefaultAsync(payment => payment.Id == id, cancellationToken);

    private async Task SetPermissionsAsync(CancellationToken cancellationToken)
    {
        ViewData["CanCreate"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.CreateCustomerPayment, cancellationToken);
        ViewData["CanEdit"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.EditCustomerPayment, cancellationToken);
        ViewData["CanDelete"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.DeleteCustomerPayment, cancellationToken);
        ViewData["CanAllocate"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.AllocateCustomerPayment, cancellationToken);
    }

    private async Task ValidateAsync(
        CustomerPaymentInputViewModel input,
        CustomerPayment? existing,
        CancellationToken cancellationToken)
    {
        if (input.CustomerId is { } customerId &&
            !await dbContext.Customers.AnyAsync(customer => customer.Id == customerId, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.CustomerId), "Select an active customer.");
        }
        if (input.BankId is { } bankId &&
            !await dbContext.Banks.AnyAsync(bank => bank.Id == bankId, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.BankId), "Select an active bank.");
        }
        if (existing is not null)
        {
            if (input.CustomerId != existing.CustomerId && existing.Allocations.Count > 0)
            {
                ModelState.AddModelError(
                    nameof(input.CustomerId), "Remove the allocations before changing the customer.");
            }
            if (input.Amount is { } amount &&
                CustomerPaymentService.ValidateAmountChange(existing, amount) is { } error)
            {
                ModelState.AddModelError(nameof(input.Amount), error);
            }
        }
    }

    private static void Apply(CustomerPayment payment, CustomerPaymentInputViewModel input)
    {
        payment.CustomerId = input.CustomerId!.Value;
        payment.PaymentDate = input.PaymentDate!.Value.Date;
        payment.Amount = decimal.Round(input.Amount!.Value, 4, MidpointRounding.AwayFromZero);
        payment.Method = input.Method!.Value;
        payment.Reference = string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim();
        payment.BankId = input.BankId;
        payment.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
    }

    private async Task PopulateOptionsAsync(
        CustomerPaymentInputViewModel model,
        CancellationToken cancellationToken)
    {
        var customers = await dbContext.Customers.AsNoTracking()
            .OrderBy(customer => customer.Name).ToListAsync(cancellationToken);
        var banks = await dbContext.Banks.AsNoTracking()
            .OrderBy(bank => bank.Name).ToListAsync(cancellationToken);
        ViewData["Customers"] = new SelectList(customers, nameof(Customer.Id), nameof(Customer.Name), model.CustomerId);
        ViewData["Banks"] = new SelectList(banks, nameof(Bank.Id), nameof(Bank.Name), model.BankId);
        ViewData["Methods"] = Enum.GetValues<PaymentMethod>()
            .Select(method => new SelectListItem(
                method.ToString(),
                method.ToString(),
                model.Method == method))
            .ToList();
    }
}
