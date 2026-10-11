using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class ProductsController(IUnitOfWork unitOfWork, AppDbContext dbContext) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewProduct)]
    public async Task<IActionResult> Index(string? searchTerm, CancellationToken cancellationToken)
    {
        var normalizedSearchTerm = searchTerm?.Trim().ToUpperInvariant();
        var productsQuery = dbContext.Products
            .IgnoreQueryFilters()
            .Where(product => !product.IsDeleted)
            .Include(product => product.Uom)
            .Include(product => product.ProductCategory)
            .AsNoTracking();

        if (!string.IsNullOrEmpty(normalizedSearchTerm))
        {
            productsQuery = productsQuery.Where(product =>
                product.Name.ToUpper().Contains(normalizedSearchTerm)
                || product.Code.ToUpper().Contains(normalizedSearchTerm)
                || product.Uom.Name.ToUpper().Contains(normalizedSearchTerm)
                || product.ProductCategory.Name.ToUpper().Contains(normalizedSearchTerm));
        }

        var products = await productsQuery
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Code)
            .ToListAsync(cancellationToken);

        return View(new ProductsIndexViewModel
        {
            SearchTerm = searchTerm?.Trim() ?? string.Empty,
            Products = products
        });
    }

    [RequireAction(AuthorizationActionCodes.CreateProduct)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(await CreateFormAsync(new ProductFormViewModel(), null, null, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateProduct)]
    public async Task<IActionResult> Create(
        [Bind("Name,Code,UomId,ProductCategoryId")] ProductFormViewModel input,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidate(input);
        if (!ModelState.IsValid)
            return View(await CreateFormAsync(input, null, null, cancellationToken));

        if (!await ActiveUomExistsAsync(input.UomId, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.UomId), "Select an active unit of measure.");
            return View(await CreateFormAsync(input, null, null, cancellationToken));
        }

        var category = await FindCategoryAsync(input.ProductCategoryId, cancellationToken);
        if (category is null)
        {
            ModelState.AddModelError(nameof(input.ProductCategoryId), "Select an active product category.");
            return View(await CreateFormAsync(input, null, null, cancellationToken));
        }

        var code = BuildCode(category, input.Code);
        if (await CodeIsReservedAsync(code, null, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.Code), "A product with this code already exists.");
            return View(await CreateFormAsync(input, null, null, cancellationToken));
        }

        var product = new Product
        {
            Name = input.Name,
            Code = code,
            UomId = input.UomId,
            ProductCategoryId = category.Id
        };
        await unitOfWork.Repository<Product>().AddAsync(product, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(input.Code), "A product with this code already exists.");
            return View(await CreateFormAsync(input, null, null, cancellationToken));
        }

        return RedirectToAction(nameof(Index));
    }

    [RequireAction(AuthorizationActionCodes.EditProduct)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var product = await FindActiveProductAsync(id, cancellationToken);
        if (product is null)
            return NotFound();

        var input = new ProductFormViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Code = product.Code[1..],
            UomId = product.UomId,
            ProductCategoryId = product.ProductCategoryId
        };
        return View(await CreateFormAsync(input, product.UomId, product.ProductCategoryId, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditProduct)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name,Code,UomId,ProductCategoryId")] ProductFormViewModel input,
        CancellationToken cancellationToken)
    {
        input.Id = id;
        NormalizeAndValidate(input);

        var product = await FindActiveProductAsync(id, cancellationToken);
        if (product is null)
            return NotFound();

        if (!ModelState.IsValid)
            return View(await CreateFormAsync(input, product.UomId, product.ProductCategoryId, cancellationToken));

        if (input.UomId != product.UomId &&
            !await ActiveUomExistsAsync(input.UomId, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.UomId), "Select an active unit of measure.");
            return View(await CreateFormAsync(input, product.UomId, product.ProductCategoryId, cancellationToken));
        }

        var category = input.ProductCategoryId == product.ProductCategoryId
            ? product.ProductCategory
            : await FindCategoryAsync(input.ProductCategoryId, cancellationToken);
        if (category is null)
        {
            ModelState.AddModelError(nameof(input.ProductCategoryId), "Select an active product category.");
            return View(await CreateFormAsync(input, product.UomId, product.ProductCategoryId, cancellationToken));
        }

        var code = BuildCode(category, input.Code);
        if (await CodeIsReservedAsync(code, id, cancellationToken))
        {
            ModelState.AddModelError(nameof(input.Code), "A product with this code already exists.");
            return View(await CreateFormAsync(input, product.UomId, product.ProductCategoryId, cancellationToken));
        }

        product.Name = input.Name;
        product.Code = code;
        product.UomId = input.UomId;
        product.ProductCategoryId = category.Id;
        unitOfWork.Repository<Product>().Update(product);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            ModelState.AddModelError(nameof(input.Code), "A product with this code already exists.");
            return View(await CreateFormAsync(input, product.UomId, product.ProductCategoryId, cancellationToken));
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteProduct)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var product = await FindActiveProductAsync(id, cancellationToken);
        if (product is null)
            return NotFound();

        product.IsDeleted = true;
        unitOfWork.Repository<Product>().Update(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private async Task<Product?> FindActiveProductAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.Products
            .IgnoreQueryFilters()
            .Include(product => product.Uom)
            .Include(product => product.ProductCategory)
            .SingleOrDefaultAsync(
                product => product.Id == id && !product.IsDeleted,
                cancellationToken);

    private Task<bool> ActiveUomExistsAsync(int uomId, CancellationToken cancellationToken) =>
        dbContext.Uoms.AnyAsync(uom => uom.Id == uomId, cancellationToken);

    private Task<bool> CodeIsReservedAsync(
        string code,
        int? exceptId,
        CancellationToken cancellationToken) =>
        dbContext.Products
            .IgnoreQueryFilters()
            .AnyAsync(
                product => EF.Property<string>(product, "NormalizedCode") == code.Trim().ToUpperInvariant()
                    && (!exceptId.HasValue || product.Id != exceptId.Value),
                cancellationToken);

    private async Task<ProductFormViewModel> CreateFormAsync(
        ProductFormViewModel input,
        int? includeDeletedUomId,
        int? includeDeletedCategoryId,
        CancellationToken cancellationToken)
    {
        var uoms = await dbContext.Uoms
            .IgnoreQueryFilters()
            .Where(uom => !uom.IsDeleted || uom.Id == includeDeletedUomId)
            .OrderBy(uom => uom.Name)
            .Select(uom => new { uom.Id, uom.Name, uom.IsDeleted })
            .ToListAsync(cancellationToken);

        input.Uoms = uoms
            .Select(uom => new SelectListItem
            {
                Value = uom.Id.ToString(),
                Text = uom.IsDeleted ? $"{uom.Name} (deleted)" : uom.Name,
                Selected = uom.Id == input.UomId
            })
            .ToList();

        var categories = await dbContext.ProductCategories
            .IgnoreQueryFilters()
            .Where(category => !category.IsDeleted || category.Id == includeDeletedCategoryId)
            .OrderBy(category => category.Name)
            .Select(category => new { category.Id, category.Name, category.Prefix, category.IsDeleted })
            .ToListAsync(cancellationToken);

        input.ProductCategories = categories
            .Select(category => new SelectListItem
            {
                Value = category.Id.ToString(),
                Text = $"{category.Name} ({category.Prefix})" + (category.IsDeleted ? " (deleted)" : string.Empty),
                Selected = category.Id == input.ProductCategoryId
            })
            .ToList();
        return input;
    }

    private void NormalizeAndValidate(ProductFormViewModel input)
    {
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.Code = input.Code?.Trim() ?? string.Empty;
        var codeIsValid = input.Code.Length is >= 1 and <= 4 && input.Code.All(char.IsAsciiDigit);
        if (codeIsValid)
            input.Code = input.Code.PadLeft(4, '0');
        ModelState.Remove(nameof(input.Name));
        ModelState.Remove(nameof(input.Code));
        TryValidateModel(input);
        if (!codeIsValid)
            ModelState.AddModelError(nameof(input.Code), "Enter a number of 1 to 4 digits.");
    }

    private async Task<ProductCategory?> FindCategoryAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.ProductCategories.SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    private static string BuildCode(ProductCategory category, string number) =>
        category.Prefix.Trim().ToUpperInvariant() + number;

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
