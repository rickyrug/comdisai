using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.Controllers;

public class CustomersController(IUnitOfWork unitOfWork) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewCustomer)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var customers = await unitOfWork.Repository<Customer>().GetAllAsync(cancellationToken);
        return View(customers.OrderBy(customer => customer.Name).ToList());
    }

    [RequireAction(AuthorizationActionCodes.CreateCustomer)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        await PopulateTaxesAsync(null, cancellationToken);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateCustomer)]
    public async Task<IActionResult> Create(
        [Bind("Name,Address,RFC,DefaultTaxId")] Customer customer,
        CancellationToken cancellationToken)
    {
        await ValidateDefaultTaxAsync(customer.DefaultTaxId, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateTaxesAsync(customer.DefaultTaxId, cancellationToken);
            return View(customer);
        }

        await unitOfWork.Repository<Customer>().AddAsync(customer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [RequireAction(AuthorizationActionCodes.EditCustomer)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(id, cancellationToken);
        if (customer is null)
            return NotFound();
        await PopulateTaxesAsync(customer.DefaultTaxId, cancellationToken);
        return View(customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditCustomer)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name,Address,RFC,DefaultTaxId")] Customer input,
        CancellationToken cancellationToken)
    {
        await ValidateDefaultTaxAsync(input.DefaultTaxId, cancellationToken);
        if (!ModelState.IsValid)
        {
            input.Id = id;
            await PopulateTaxesAsync(input.DefaultTaxId, cancellationToken);
            return View(input);
        }

        var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(id, cancellationToken);
        if (customer is null)
            return NotFound();

        customer.Name = input.Name;
        customer.Address = input.Address;
        customer.RFC = input.RFC;
        customer.DefaultTaxId = input.DefaultTaxId;
        unitOfWork.Repository<Customer>().Update(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteCustomer)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(id, cancellationToken);
        if (customer is null)
            return NotFound();

        customer.IsDeleted = true;
        unitOfWork.Repository<Customer>().Update(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateTaxesAsync(int? selectedId, CancellationToken cancellationToken)
    {
        var taxes = await unitOfWork.Repository<Tax>().GetAllAsync(cancellationToken);
        ViewData["Taxes"] = new SelectList(
            taxes.OrderBy(tax => tax.Name).Select(tax => new
            {
                tax.Id,
                Text = $"{tax.Name} ({(tax.Value * 100m).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}%)"
            }),
            "Id", "Text", selectedId);
    }

    private async Task ValidateDefaultTaxAsync(int? taxId, CancellationToken cancellationToken)
    {
        if (taxId is null)
            return;
        var tax = await unitOfWork.Repository<Tax>().GetByIdAsync(taxId.Value, cancellationToken);
        if (tax is null)
            ModelState.AddModelError(nameof(Customer.DefaultTaxId), "Select a valid tax.");
    }
}
