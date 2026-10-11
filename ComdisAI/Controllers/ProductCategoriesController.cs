using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class ProductCategoriesController(IUnitOfWork unitOfWork, AppDbContext dbContext) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewProductCategory)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var categories = await unitOfWork.Repository<ProductCategory>().GetAllAsync(cancellationToken);
        return View(categories.OrderBy(category => category.Name).ToList());
    }

    [RequireAction(AuthorizationActionCodes.CreateProductCategory)]
    public IActionResult Create() => View(new ProductCategory { Name = string.Empty, Prefix = string.Empty });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateProductCategory)]
    public async Task<IActionResult> Create(
        [Bind("Name,Prefix")] ProductCategory category,
        CancellationToken cancellationToken)
    {
        NormalizeAndValidate(category);
        if (!ModelState.IsValid)
            return View(category);

        await AddDuplicateErrorsAsync(category, null, cancellationToken);
        if (!ModelState.IsValid)
            return View(category);

        await unitOfWork.Repository<ProductCategory>().AddAsync(category, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await AddDuplicateErrorsAsync(category, null, cancellationToken);
            if (!ModelState.IsValid)
                return View(category);

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [RequireAction(AuthorizationActionCodes.EditProductCategory)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var category = await unitOfWork.Repository<ProductCategory>().GetByIdAsync(id, cancellationToken);
        return category is null ? NotFound() : View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.EditProductCategory)]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Name,Prefix")] ProductCategory input,
        CancellationToken cancellationToken)
    {
        input.Id = id;
        NormalizeAndValidate(input);
        if (!ModelState.IsValid)
            return View(input);

        var category = await unitOfWork.Repository<ProductCategory>().GetByIdAsync(id, cancellationToken);
        if (category is null)
            return NotFound();

        await AddDuplicateErrorsAsync(input, id, cancellationToken);
        if (!ModelState.IsValid)
            return View(input);

        category.Name = input.Name;
        category.Prefix = input.Prefix;
        unitOfWork.Repository<ProductCategory>().Update(category);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await AddDuplicateErrorsAsync(input, id, cancellationToken);
            if (!ModelState.IsValid)
                return View(input);

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeleteProductCategory)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var category = await unitOfWork.Repository<ProductCategory>().GetByIdAsync(id, cancellationToken);
        if (category is null)
            return NotFound();

        category.IsDeleted = true;
        unitOfWork.Repository<ProductCategory>().Update(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private void NormalizeAndValidate(ProductCategory category)
    {
        category.Name = category.Name?.Trim() ?? string.Empty;
        category.Prefix = category.Prefix?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(ProductCategory.Name));
        ModelState.Remove(nameof(ProductCategory.Prefix));
        TryValidateModel(category);
    }

    private async Task AddDuplicateErrorsAsync(
        ProductCategory category,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        var categories = dbContext.ProductCategories.IgnoreQueryFilters();
        if (await categories.AnyAsync(
                candidate => EF.Property<string>(candidate, "NormalizedName") ==
                             category.Name.Trim().ToUpperInvariant()
                             && (!exceptId.HasValue || candidate.Id != exceptId.Value),
                cancellationToken))
        {
            ModelState.AddModelError(nameof(ProductCategory.Name), "A product category with this name already exists.");
        }

        if (await categories.AnyAsync(
                candidate => EF.Property<string>(candidate, "NormalizedPrefix") ==
                             category.Prefix.Trim().ToUpperInvariant()
                             && (!exceptId.HasValue || candidate.Id != exceptId.Value),
                cancellationToken))
        {
            ModelState.AddModelError(nameof(ProductCategory.Prefix), "A product category with this prefix already exists.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
}
