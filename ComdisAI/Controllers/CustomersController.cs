using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace ComdisAI.Controllers;

public class CustomersController(IUnitOfWork unitOfWork) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var customers = await unitOfWork.Repository<Customer>().GetAllAsync(cancellationToken);
        return View(customers.OrderBy(customer => customer.Name).ToList());
    }

    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Name,Address,RFC")] Customer customer,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(customer);

        await unitOfWork.Repository<Customer>().AddAsync(customer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(id, cancellationToken);
        return customer is null ? NotFound() : View(customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name,Address,RFC")] Customer input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            input.Id = id;
            return View(input);
        }

        var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(id, cancellationToken);
        if (customer is null)
            return NotFound();

        customer.Name = input.Name;
        customer.Address = input.Address;
        customer.RFC = input.RFC;
        unitOfWork.Repository<Customer>().Update(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
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
}
