using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.CustomerPriceLists;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;

namespace ComdisAI.Controllers;

public class CustomerPriceListController(IUnitOfWork unitOfWork) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewCustomerPriceList)]
    public async Task<IActionResult> Index(int customerId, CancellationToken cancellationToken)
    {
        var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(customerId, cancellationToken);
        if (customer is null || customer.IsDeleted)
            return NotFound();

        var products = (await LoadProductsAsync(cancellationToken)).ToDictionary(product => product.Id);
        var items = (await unitOfWork.Repository<CustomerPriceList>().GetAllAsync(cancellationToken))
            .Where(item => item.CustomerId == customerId && products.ContainsKey(item.ProductId))
            .ToList();
        foreach (var item in items)
            item.Product = products[item.ProductId];

        var userNames = (await unitOfWork.Repository<User>().GetAllAsync(cancellationToken))
            .ToDictionary(
                user => user.Id.ToString(CultureInfo.InvariantCulture),
                user => $"{user.Name} {user.Surname}".Trim());

        return View(new CustomerPriceListIndexViewModel
        {
            Customer = customer,
            UserNames = userNames,
            Items = items.OrderBy(item => item.Product.Name).ToList()
        });
    }

    [RequireAction(AuthorizationActionCodes.CreateCustomerPriceList)]
    public async Task<IActionResult> Create(int customerId, CancellationToken cancellationToken)
    {
        var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(customerId, cancellationToken);
        if (customer is null || customer.IsDeleted)
            return NotFound();

        var model = new CustomerPriceListInputViewModel { CustomerId = customerId, CustomerName = customer.Name };
        await PopulateProductsAsync(model, null, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateCustomerPriceList)]
    public async Task<IActionResult> Create(
        int customerId,
        [Bind("ProductId,Price")] CustomerPriceListInputViewModel input,
        CancellationToken cancellationToken)
    {
        var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(customerId, cancellationToken);
        if (customer is null || customer.IsDeleted)
            return NotFound();

        input.CustomerId = customerId;
        input.CustomerName = customer.Name;
        await ValidateAsync(input, null, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateProductsAsync(input, null, cancellationToken);
            return View(input);
        }

        await unitOfWork.Repository<CustomerPriceList>().AddAsync(new CustomerPriceList
        {
            CustomerId = customerId,
            ProductId = input.ProductId!.Value,
            Price = input.Price!.Value
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { customerId });
    }

    [RequireAction(AuthorizationActionCodes.EditCustomerPriceList)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var item = await unitOfWork.Repository<CustomerPriceList>().GetByIdAsync(id, cancellationToken);
        var customer = item is null
            ? null
            : await unitOfWork.Repository<Customer>().GetByIdAsync(item.CustomerId, cancellationToken);
        if (item is null || customer is null || customer.IsDeleted)
            return NotFound();

        var model = new CustomerPriceListInputViewModel
        {
            Id = item.Id,
            CustomerId = item.CustomerId,
            CustomerName = customer.Name,
            ProductId = item.ProductId,
            Price = item.Price
        };
        await PopulateProductsAsync(model, item.ProductId, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditCustomerPriceList)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("ProductId,Price")] CustomerPriceListInputViewModel input,
        CancellationToken cancellationToken)
    {
        var item = await unitOfWork.Repository<CustomerPriceList>().GetByIdAsync(id, cancellationToken);
        var customer = item is null
            ? null
            : await unitOfWork.Repository<Customer>().GetByIdAsync(item.CustomerId, cancellationToken);
        if (item is null || customer is null || customer.IsDeleted)
            return NotFound();

        input.Id = id;
        input.CustomerId = item.CustomerId;
        input.CustomerName = customer.Name;
        await ValidateAsync(input, item, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateProductsAsync(input, item.ProductId, cancellationToken);
            return View(input);
        }

        item.ProductId = input.ProductId!.Value;
        item.Price = input.Price!.Value;
        unitOfWork.Repository<CustomerPriceList>().Update(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { customerId = item.CustomerId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteCustomerPriceList)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var item = await unitOfWork.Repository<CustomerPriceList>().GetByIdAsync(id, cancellationToken);
        if (item is null)
            return NotFound();

        unitOfWork.Repository<CustomerPriceList>().Delete(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { customerId = item.CustomerId });
    }

    private async Task<IReadOnlyList<Product>> LoadProductsAsync(CancellationToken cancellationToken) =>
        (await unitOfWork.Repository<Product>().GetAllAsync(cancellationToken)).ToList();

    private async Task ValidateAsync(
        CustomerPriceListInputViewModel input,
        CustomerPriceList? existing,
        CancellationToken cancellationToken)
    {
        if (input.ProductId is null)
            return;

        var product = await unitOfWork.Repository<Product>().GetByIdAsync(input.ProductId.Value, cancellationToken);
        if (product is null || product.IsDeleted)
        {
            ModelState.AddModelError(nameof(input.ProductId), "Select an active product.");
            return;
        }

        var duplicate = (await unitOfWork.Repository<CustomerPriceList>().GetAllAsync(cancellationToken))
            .Any(item => item.CustomerId == input.CustomerId &&
                         item.ProductId == input.ProductId &&
                         item.Id != existing?.Id);
        if (duplicate)
            ModelState.AddModelError(nameof(input.ProductId), "This product already has a price for the customer.");
    }

    private async Task PopulateProductsAsync(
        CustomerPriceListInputViewModel model,
        int? currentProductId,
        CancellationToken cancellationToken)
    {
        var products = await LoadProductsAsync(cancellationToken);
        model.Products = products
            .OrderBy(product => product.Name)
            .Select(product => new SelectListItem(
                $"{product.Code} - {product.Name}",
                product.Id.ToString(CultureInfo.InvariantCulture),
                product.Id == (model.ProductId ?? currentProductId)))
            .ToList();
    }
}
