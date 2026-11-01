using System.Globalization;
using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.SupplierPriceLists;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.Controllers;

public class SupplierPriceListController(IUnitOfWork unitOfWork) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewSupplierPriceList)]
    public async Task<IActionResult> Index(int supplierId, CancellationToken cancellationToken)
    {
        var supplier = await unitOfWork.Repository<Supplier>().GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null || supplier.IsDeleted)
            return NotFound();

        var products = (await LoadProductsAsync(cancellationToken)).ToDictionary(product => product.Id);
        var items = (await unitOfWork.Repository<SupplierPriceList>().GetAllAsync(cancellationToken))
            .Where(item => item.SupplierId == supplierId && products.ContainsKey(item.ProductId))
            .ToList();
        foreach (var item in items)
            item.Product = products[item.ProductId];

        var userNames = (await unitOfWork.Repository<User>().GetAllAsync(cancellationToken))
            .ToDictionary(
                user => user.Id.ToString(CultureInfo.InvariantCulture),
                user => $"{user.Name} {user.Surname}".Trim());

        return View(new SupplierPriceListIndexViewModel
        {
            Supplier = supplier,
            UserNames = userNames,
            Items = items.OrderBy(item => item.Product.Name).ToList()
        });
    }

    [RequireAction(AuthorizationActionCodes.CreateSupplierPriceList)]
    public async Task<IActionResult> Create(int supplierId, CancellationToken cancellationToken)
    {
        var supplier = await unitOfWork.Repository<Supplier>().GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null || supplier.IsDeleted)
            return NotFound();

        var model = new SupplierPriceListInputViewModel { SupplierId = supplierId, SupplierName = supplier.Name };
        await PopulateProductsAsync(model, null, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateSupplierPriceList)]
    public async Task<IActionResult> Create(
        int supplierId,
        [Bind("ProductId,Price")] SupplierPriceListInputViewModel input,
        CancellationToken cancellationToken)
    {
        var supplier = await unitOfWork.Repository<Supplier>().GetByIdAsync(supplierId, cancellationToken);
        if (supplier is null || supplier.IsDeleted)
            return NotFound();

        input.SupplierId = supplierId;
        input.SupplierName = supplier.Name;
        await ValidateAsync(input, null, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateProductsAsync(input, null, cancellationToken);
            return View(input);
        }

        await unitOfWork.Repository<SupplierPriceList>().AddAsync(new SupplierPriceList
        {
            SupplierId = supplierId,
            ProductId = input.ProductId!.Value,
            Price = input.Price!.Value
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { supplierId });
    }

    [RequireAction(AuthorizationActionCodes.EditSupplierPriceList)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var item = await unitOfWork.Repository<SupplierPriceList>().GetByIdAsync(id, cancellationToken);
        var supplier = item is null
            ? null
            : await unitOfWork.Repository<Supplier>().GetByIdAsync(item.SupplierId, cancellationToken);
        if (item is null || supplier is null || supplier.IsDeleted)
            return NotFound();

        var model = new SupplierPriceListInputViewModel
        {
            Id = item.Id,
            SupplierId = item.SupplierId,
            SupplierName = supplier.Name,
            ProductId = item.ProductId,
            Price = item.Price
        };
        await PopulateProductsAsync(model, item.ProductId, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditSupplierPriceList)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("ProductId,Price")] SupplierPriceListInputViewModel input,
        CancellationToken cancellationToken)
    {
        var item = await unitOfWork.Repository<SupplierPriceList>().GetByIdAsync(id, cancellationToken);
        var supplier = item is null
            ? null
            : await unitOfWork.Repository<Supplier>().GetByIdAsync(item.SupplierId, cancellationToken);
        if (item is null || supplier is null || supplier.IsDeleted)
            return NotFound();

        input.Id = id;
        input.SupplierId = item.SupplierId;
        input.SupplierName = supplier.Name;
        await ValidateAsync(input, item, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateProductsAsync(input, item.ProductId, cancellationToken);
            return View(input);
        }

        item.ProductId = input.ProductId!.Value;
        item.Price = input.Price!.Value;
        unitOfWork.Repository<SupplierPriceList>().Update(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { supplierId = item.SupplierId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteSupplierPriceList)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var item = await unitOfWork.Repository<SupplierPriceList>().GetByIdAsync(id, cancellationToken);
        if (item is null)
            return NotFound();

        unitOfWork.Repository<SupplierPriceList>().Delete(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { supplierId = item.SupplierId });
    }

    private async Task<IReadOnlyList<Product>> LoadProductsAsync(CancellationToken cancellationToken) =>
        (await unitOfWork.Repository<Product>().GetAllAsync(cancellationToken)).ToList();

    private async Task ValidateAsync(
        SupplierPriceListInputViewModel input,
        SupplierPriceList? existing,
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

        var duplicate = (await unitOfWork.Repository<SupplierPriceList>().GetAllAsync(cancellationToken))
            .Any(item => item.SupplierId == input.SupplierId &&
                         item.ProductId == input.ProductId &&
                         item.Id != existing?.Id);
        if (duplicate)
            ModelState.AddModelError(nameof(input.ProductId), "This product already has a price for the supplier.");
    }

    private async Task PopulateProductsAsync(
        SupplierPriceListInputViewModel model,
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