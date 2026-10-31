using System.Data;
using System.Globalization;
using System.Security.Claims;
using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.Services;
using ComdisAI.ViewModels.PurchaseOrders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class PurchaseOrdersController(
    IUnitOfWork unitOfWork,
    AppDbContext dbContext,
    IActionPermissionService permissionService,
    PurchaseOrderPdfService pdfService) : Controller
{
    private const string PurchaseOrderEntity = "PurchaseOrder";
    private const string DraftStatus = "Draft";
    private const string ApprovedStatus = "Approved";
    private const string SuppliedStatus = "Supplied";
    private const string CancelledStatus = "Cancelled";

    [RequireAction(AuthorizationActionCodes.ViewPurchaseOrder)]
    public async Task<IActionResult> Index(
        string? q,
        int? statusId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PurchaseOrderHeaders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.Status)
            .Include(order => order.Details)
            .AsQueryable();
        var term = q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(order =>
                order.OrderNumber.Contains(term) ||
                order.Supplier.Name.Contains(term) ||
                order.Status.Name.Contains(term));
        }
        if (statusId is not null)
            query = query.Where(order => order.StatusId == statusId);

        var orders = await query
            .OrderByDescending(order => order.OrderNumber)
            .ToListAsync(cancellationToken);
        var statuses = await dbContext.Statuses
            .AsNoTracking()
            .Where(status => EF.Property<string>(status, "NormalizedEntity") == PurchaseOrderEntity.ToUpperInvariant())
            .OrderBy(status => status.Name)
            .ToListAsync(cancellationToken);
        ViewData["Statuses"] = new SelectList(statuses, nameof(Status.Id), nameof(Status.Name), statusId);
        ViewData["Query"] = term;
        ViewData["StatusId"] = statusId;
        ViewData["CanApprove"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.ApprovePurchaseOrder, cancellationToken);
        ViewData["CanSupply"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.SupplyPurchaseOrder, cancellationToken);
        ViewData["CanPrint"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.PrintPurchaseOrder, cancellationToken);
        ViewData["CanCreate"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.CreatePurchaseOrder, cancellationToken);
        return View(orders);
    }

    [RequireAction(AuthorizationActionCodes.ViewPurchaseOrder)]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();

        var hasDetails = order.Details.Count > 0;
        var isSupplied = IsStatus(order.Status, SuppliedStatus);
        ViewData["CanEdit"] = !IsStatus(order.Status, CancelledStatus) && await permissionService.HasActionAsync(
            User,
            isSupplied
                ? AuthorizationActionCodes.EditSuppliedPurchaseOrder
                : AuthorizationActionCodes.EditPurchaseOrder,
            cancellationToken);
        ViewData["CanApprove"] = hasDetails &&
            IsStatus(order.Status, DraftStatus) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.ApprovePurchaseOrder, cancellationToken);
        ViewData["CanSupply"] = IsStatus(order.Status, ApprovedStatus) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.SupplyPurchaseOrder, cancellationToken);
        ViewData["CanCancel"] = (IsStatus(order.Status, DraftStatus) || IsStatus(order.Status, ApprovedStatus)) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.CancelPurchaseOrder, cancellationToken);
        ViewData["CanPrint"] = hasDetails &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.PrintPurchaseOrder, cancellationToken);
        ViewData["ReceivedByName"] = await ResolveUserNameAsync(order.ReceivedBy, cancellationToken);
        return View(order);
    }

    private async Task<string?> ResolveUserNameAsync(string? userId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(userId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return userId;

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new { candidate.Name, candidate.Surname })
            .FirstOrDefaultAsync(cancellationToken);
        return user is null ? userId : $"{user.Name} {user.Surname}".Trim();
    }

    [HttpGet]
    [RequireAction(AuthorizationActionCodes.CreatePurchaseOrder)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new PurchaseOrderHeaderInputViewModel();
        await PopulateHeaderOptionsAsync(model, cancellationToken);
        AddMissingStatusErrors(await GetRequiredStatusesAsync(cancellationToken));
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreatePurchaseOrder)]
    public async Task<IActionResult> Create(
        PurchaseOrderHeaderInputViewModel input,
        CancellationToken cancellationToken)
    {
        ValidateHeaderInput(input);
        var statuses = await GetRequiredStatusesAsync(cancellationToken);
        AddMissingStatusErrors(statuses);
        var supplier = await dbContext.Suppliers
            .SingleOrDefaultAsync(row => row.Id == input.SupplierId, cancellationToken);
        if (supplier is null)
            ModelState.AddModelError(nameof(input.SupplierId), "Select an active supplier.");
        var tax = await dbContext.Taxes
            .SingleOrDefaultAsync(row => row.Id == input.TaxId, cancellationToken);
        ValidateTax(tax, nameof(input.TaxId));

        if (!ModelState.IsValid || supplier is null || tax is null || statuses is null)
        {
            await PopulateHeaderOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var year = DateTime.UtcNow.Year;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existingNumbers = await dbContext.PurchaseOrderHeaders
            .AsNoTracking()
            .Where(order => order.OrderNumber.StartsWith(
                year.ToString("0000", CultureInfo.InvariantCulture) + "-"))
            .Select(order => order.OrderNumber)
            .ToListAsync(cancellationToken);
        var header = new PurchaseOrderHeader
        {
            OrderNumber = PurchaseOrderNumberGenerator.NextNumber(year, existingNumbers),
            SupplierId = supplier.Id,
            SupplyAddress = input.SupplyAddress.Trim(),
            SupplyDate = input.SupplyDate.Date,
            TaxId = tax.Id,
            StatusId = statuses[DraftStatus].Id,
            DiscountPercent1 = input.DiscountPercent1,
            DiscountPercent2 = input.DiscountPercent2,
            DiscountPercent3 = input.DiscountPercent3,
            Tax = tax
        };
        PurchaseOrderPricing.Recalculate(header);
        await unitOfWork.Repository<PurchaseOrderHeader>().AddAsync(header, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return RedirectToAction(nameof(Edit), new { id = header.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (!await CanEditAsync(order, cancellationToken))
            return AccessDenied();

        var model = MapToEditViewModel(order);
        await PopulateEditOptionsAsync(model, cancellationToken);
        await PopulateStatusActionsAsync(order, cancellationToken);
        AddMissingStatusErrors(await GetRequiredStatusesAsync(cancellationToken));
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        PurchaseOrderEditViewModel input,
        CancellationToken cancellationToken)
    {
        input.Details ??= [];
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (!await CanEditAsync(order, cancellationToken))
            return AccessDenied();

        input.Id = id;
        ValidateHeaderInput(input);
        ValidateDetailsInput(input.Details);
        var statuses = await GetRequiredStatusesAsync(cancellationToken);
        AddMissingStatusErrors(statuses);
        var supplier = await dbContext.Suppliers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.Id == input.SupplierId, cancellationToken);
        if (supplier is null || (supplier.IsDeleted && supplier.Id != order.SupplierId))
            ModelState.AddModelError(nameof(input.SupplierId), "Select an active supplier.");
        var tax = await dbContext.Taxes
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.Id == input.TaxId, cancellationToken);
        if (tax?.IsDeleted == true && tax.Id != order.TaxId)
            tax = null;
        ValidateTax(tax, nameof(input.TaxId));

        var existingDetailsById = order.Details.ToDictionary(detail => detail.Id);
        var submittedProductIds = input.Details.Select(detail => detail.ProductId).Distinct().ToArray();
        var products = await dbContext.Products
            .IgnoreQueryFilters()
            .Where(product => submittedProductIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        for (var index = 0; index < input.Details.Count; index++)
        {
            var detailInput = input.Details[index];
            var mayKeepDeletedProduct = detailInput.Id.HasValue &&
                existingDetailsById.TryGetValue(detailInput.Id.Value, out var existingDetail) &&
                existingDetail.ProductId == detailInput.ProductId;
            if (!products.TryGetValue(detailInput.ProductId, out var product) ||
                (product.IsDeleted && !mayKeepDeletedProduct))
            {
                ModelState.AddModelError($"Details[{index}].ProductId", "Select an active product.");
            }
        }

        var submittedIds = input.Details
            .Where(detail => detail.Id.HasValue)
            .Select(detail => detail.Id!.Value)
            .ToArray();
        if (submittedIds.Distinct().Count() != submittedIds.Length ||
            submittedIds.Any(detailId => !existingDetailsById.ContainsKey(detailId)))
        {
            ModelState.AddModelError(nameof(input.Details), "One or more submitted purchase order lines are invalid.");
        }

        if (!ModelState.IsValid || supplier is null || tax is null || statuses is null)
        {
            input.OrderNumber = order.OrderNumber;
            input.StatusName = order.Status.Name;
            await PopulateEditOptionsAsync(input, cancellationToken);
            await PopulateStatusActionsAsync(order, cancellationToken);
            return View(input);
        }

        order.SupplierId = supplier.Id;
        order.SupplyAddress = input.SupplyAddress.Trim();
        order.SupplyDate = input.SupplyDate.Date;
        order.TaxId = tax.Id;
        order.Tax = tax;
        order.DiscountPercent1 = input.DiscountPercent1;
        order.DiscountPercent2 = input.DiscountPercent2;
        order.DiscountPercent3 = input.DiscountPercent3;

        var retainedIds = submittedIds.ToHashSet();
        foreach (var existingDetail in order.Details.Where(detail => !retainedIds.Contains(detail.Id)).ToList())
        {
            unitOfWork.Repository<PurchaseOrderDetail>().Delete(existingDetail);
            order.Details.Remove(existingDetail);
        }

        foreach (var detailInput in input.Details)
        {
            var detail = detailInput.Id.HasValue
                ? existingDetailsById[detailInput.Id.Value]
                : new PurchaseOrderDetail();
            detail.ProductId = detailInput.ProductId;
            detail.Qty = detailInput.Qty;
            detail.Price = detailInput.Price;
            detail.DiscountPercent = detailInput.DiscountPercent;

            if (!detailInput.Id.HasValue)
                order.Details.Add(detail);
        }

        if (!IsStatus(order.Status, SuppliedStatus))
        {
            order.StatusId = statuses[DraftStatus].Id;
            order.Status = statuses[DraftStatus];
        }

        try
        {
            PurchaseOrderPricing.Recalculate(order);
        }
        catch (Exception exception) when (
            exception is ArgumentOutOfRangeException or OverflowException)
        {
            ModelState.AddModelError(string.Empty, "Calculated amounts exceed the supported decimal(18,4) range.");
            input.OrderNumber = order.OrderNumber;
            input.StatusName = order.Status.Name;
            await PopulateEditOptionsAsync(input, cancellationToken);
            await PopulateStatusActionsAsync(order, cancellationToken);
            return View(input);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Purchase order saved. Any previous approval was reset to Draft.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.ApprovePurchaseOrder)]
    public async Task<IActionResult> Approve(int id, string? returnTo, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (!IsStatus(order.Status, DraftStatus) || order.Details.Count == 0)
        {
            TempData["Error"] = "Only a Draft purchase order with at least one line can be approved.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        var statuses = await GetRequiredStatusesAsync(cancellationToken);
        if (statuses is null)
        {
            TempData["Error"] = "PurchaseOrder statuses are not configured. Add Draft, Approved, and Supplied statuses.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        order.StatusId = statuses[ApprovedStatus].Id;
        order.Status = statuses[ApprovedStatus];
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Purchase order approved.";
        return RedirectAfterStatusChange(id, returnTo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.SupplyPurchaseOrder)]
    public async Task<IActionResult> Supply(int id, string? returnTo, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (!IsStatus(order.Status, ApprovedStatus) || order.Details.Count == 0)
        {
            TempData["Error"] = "Only an Approved purchase order with at least one line can be marked as Supplied.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        var statuses = await GetRequiredStatusesAsync(cancellationToken);
        if (statuses is null)
        {
            TempData["Error"] = "PurchaseOrder statuses are not configured. Add Draft, Approved, and Supplied statuses.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        order.StatusId = statuses[SuppliedStatus].Id;
        order.Status = statuses[SuppliedStatus];
        order.ReceivedAtUtc = DateTime.UtcNow;
        order.ReceivedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "system";
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Purchase order marked as Supplied.";
        return RedirectAfterStatusChange(id, returnTo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CancelPurchaseOrder)]
    public async Task<IActionResult> Cancel(int id, string? returnTo, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (!IsStatus(order.Status, DraftStatus) && !IsStatus(order.Status, ApprovedStatus))
        {
            TempData["Error"] = "Only a Draft or Approved purchase order can be cancelled.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        var cancelled = await dbContext.Statuses
            .FirstOrDefaultAsync(status =>
                EF.Property<string>(status, "NormalizedEntity") == PurchaseOrderEntity.ToUpperInvariant() &&
                EF.Property<string>(status, "NormalizedName") == CancelledStatus.ToUpperInvariant(),
                cancellationToken);
        if (cancelled is null)
        {
            TempData["Error"] = "PurchaseOrder status 'Cancelled' is not configured. Add it in Statuses first.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        order.StatusId = cancelled.Id;
        order.Status = cancelled;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Purchase order cancelled.";
        return RedirectAfterStatusChange(id, returnTo);
    }

    [RequireAction(AuthorizationActionCodes.PrintPurchaseOrder)]
    public async Task<IActionResult> Print(int id, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (order.Details.Count == 0)
        {
            TempData["Error"] = "A purchase order must have at least one line before it can be printed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var pdf = pdfService.Create(order);
        return File(pdf, "application/pdf", $"PurchaseOrder-{order.OrderNumber}.pdf");
    }

    private IActionResult RedirectAfterStatusChange(int id, string? returnTo) =>
        returnTo == "index"
            ? RedirectToAction(nameof(Index))
            : RedirectToAction(nameof(Details), new { id });

    private async Task<PurchaseOrderHeader?> GetOrderAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.PurchaseOrderHeaders
            .IgnoreQueryFilters()
            .Include(order => order.Supplier)
            .Include(order => order.Status)
            .Include(order => order.Tax)
            .Include(order => order.Details)
                .ThenInclude(detail => detail.Product)
                    .ThenInclude(product => product.ProductCategory)
            .Include(order => order.Details)
                .ThenInclude(detail => detail.Product)
                    .ThenInclude(product => product.Uom)
            .SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

    private async Task<bool> CanEditAsync(
        PurchaseOrderHeader order,
        CancellationToken cancellationToken)
    {
        if (IsStatus(order.Status, CancelledStatus))
            return false;
        if (IsStatus(order.Status, SuppliedStatus))
            return await permissionService.HasActionAsync(
                User, AuthorizationActionCodes.EditSuppliedPurchaseOrder, cancellationToken);
        if (IsStatus(order.Status, DraftStatus) && order.Details.Count == 0 &&
            await permissionService.HasActionAsync(
                User, AuthorizationActionCodes.CreatePurchaseOrder, cancellationToken))
        {
            return true;
        }

        return await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.EditPurchaseOrder, cancellationToken);
    }

    private async Task PopulateHeaderOptionsAsync(
        PurchaseOrderHeaderInputViewModel model,
        CancellationToken cancellationToken)
    {
        model.Suppliers = await dbContext.Suppliers
            .AsNoTracking()
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new SelectListItem(supplier.Name, supplier.Id.ToString(CultureInfo.InvariantCulture)))
            .ToListAsync(cancellationToken);
        var addresses = await dbContext.Suppliers
            .AsNoTracking()
            .Select(supplier => new { supplier.Id, supplier.Address })
            .ToListAsync(cancellationToken);
        model.SupplierAddresses = addresses.ToDictionary(
            row => row.Id.ToString(CultureInfo.InvariantCulture),
            row => row.Address);
        model.Taxes = await dbContext.Taxes
            .AsNoTracking()
            .OrderBy(tax => tax.Name)
            .Select(tax => new SelectListItem(
                tax.Name + " (" + (tax.Value * 100m).ToString("0.####", CultureInfo.InvariantCulture) + "%)",
                tax.Id.ToString(CultureInfo.InvariantCulture)))
            .ToListAsync(cancellationToken);
    }

    private async Task PopulateStatusActionsAsync(PurchaseOrderHeader order, CancellationToken cancellationToken)
    {
        ViewData["CanApprove"] = order.Details.Count > 0 &&
            IsStatus(order.Status, DraftStatus) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.ApprovePurchaseOrder, cancellationToken);
        ViewData["CanSupply"] = IsStatus(order.Status, ApprovedStatus) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.SupplyPurchaseOrder, cancellationToken);
    }

    private async Task PopulateEditOptionsAsync(
        PurchaseOrderEditViewModel model,
        CancellationToken cancellationToken)
    {
        var existingReferences = await dbContext.PurchaseOrderHeaders
            .IgnoreQueryFilters()
            .Where(order => order.Id == model.Id)
            .Select(order => new { order.SupplierId, order.TaxId })
            .SingleAsync(cancellationToken);
        var suppliers = await dbContext.Suppliers
            .IgnoreQueryFilters()
            .Where(supplier => !supplier.IsDeleted || supplier.Id == existingReferences.SupplierId)
            .OrderBy(supplier => supplier.Name)
            .ToListAsync(cancellationToken);
        model.Suppliers = suppliers
            .Select(supplier => new SelectListItem(
                supplier.Name + (supplier.IsDeleted ? " (inactive)" : string.Empty),
                supplier.Id.ToString(CultureInfo.InvariantCulture)))
            .ToList();
        var taxes = await dbContext.Taxes
            .IgnoreQueryFilters()
            .Where(tax => !tax.IsDeleted || tax.Id == existingReferences.TaxId)
            .OrderBy(tax => tax.Name)
            .ToListAsync(cancellationToken);
        model.Taxes = taxes
            .Select(tax => new SelectListItem(
                tax.Name + (tax.IsDeleted ? " (inactive)" : string.Empty) + " (" +
                (tax.Value * 100m).ToString("0.####", CultureInfo.InvariantCulture) + "%)",
                tax.Id.ToString(CultureInfo.InvariantCulture)))
            .ToList();
        var existingProductIds = await dbContext.PurchaseOrderDetails
            .IgnoreQueryFilters()
            .Where(detail => detail.PurchaseOrderHeaderId == model.Id)
            .Select(detail => detail.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var products = await dbContext.Products
            .IgnoreQueryFilters()
            .Where(product => !product.IsDeleted || existingProductIds.Contains(product.Id))
            .AsNoTracking()
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
        model.Products = products
            .Select(product => new SelectListItem(
                product.Code + " - " + product.Name + (product.IsDeleted ? " (inactive)" : string.Empty),
                product.Id.ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }

    private async Task<Dictionary<string, Status>?> GetRequiredStatusesAsync(
        CancellationToken cancellationToken)
    {
        var requiredNames = new[] { DraftStatus, ApprovedStatus, SuppliedStatus };
        var normalizedNames = requiredNames.Select(name => name.ToUpperInvariant()).ToArray();
        var statuses = await dbContext.Statuses
            .Where(status =>
                EF.Property<string>(status, "NormalizedEntity") == PurchaseOrderEntity.ToUpperInvariant() &&
                normalizedNames.Contains(EF.Property<string>(status, "NormalizedName")))
            .ToListAsync(cancellationToken);
        var byName = statuses.ToDictionary(status => status.Name, StringComparer.OrdinalIgnoreCase);
        return requiredNames.All(byName.ContainsKey) ? byName : null;
    }

    private void AddMissingStatusErrors(Dictionary<string, Status>? statuses)
    {
        if (statuses is not null)
            return;

        ModelState.AddModelError(
            string.Empty,
            "PurchaseOrder status configuration is incomplete. Add active Draft, Approved, and Supplied statuses.");
    }

    private void ValidateHeaderInput(PurchaseOrderHeaderInputViewModel input)
    {
        input.SupplyAddress = input.SupplyAddress?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(input.SupplyAddress));
        TryValidateModel(input);
        if (input.SupplyDate == default)
            ModelState.AddModelError(nameof(input.SupplyDate), "A supply date is required.");
        ValidatePrecision(input.DiscountPercent1, nameof(input.DiscountPercent1));
        ValidatePrecision(input.DiscountPercent2, nameof(input.DiscountPercent2));
        ValidatePrecision(input.DiscountPercent3, nameof(input.DiscountPercent3));
    }

    private void ValidateDetailsInput(IReadOnlyList<PurchaseOrderDetailInputViewModel> details)
    {
        for (var index = 0; index < details.Count; index++)
        {
            var detail = details[index];
            ValidatePrecision(detail.Qty, $"Details[{index}].Qty");
            ValidatePrecision(detail.Price, $"Details[{index}].Price");
            if (detail.DiscountPercent.HasValue)
                ValidatePrecision(detail.DiscountPercent.Value, $"Details[{index}].DiscountPercent");
        }
    }

    private void ValidateTax(Tax? tax, string key)
    {
        if (tax is null)
        {
            ModelState.AddModelError(key, "Select an active tax.");
            return;
        }

        if (tax.Value < 0m || tax.Value > 1m)
            ModelState.AddModelError(key, "Tax value must be a fraction between 0 and 1 (0.16 means 16%).");
    }

    private void ValidatePrecision(decimal value, string key)
    {
        if (value > 99_999_999_999_999.9999m || decimal.Round(value, 4) != value)
            ModelState.AddModelError(key, "Value must fit decimal(18,4).");
    }

    private static bool IsStatus(Status status, string name) =>
        string.Equals(status.Name, name, StringComparison.OrdinalIgnoreCase);

    private PurchaseOrderEditViewModel MapToEditViewModel(PurchaseOrderHeader order) =>
        new()
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            StatusName = order.Status.Name,
            SupplierId = order.SupplierId,
            SupplyAddress = order.SupplyAddress,
            SupplyDate = order.SupplyDate,
            TaxId = order.TaxId,
            DiscountPercent1 = order.DiscountPercent1,
            DiscountPercent2 = order.DiscountPercent2,
            DiscountPercent3 = order.DiscountPercent3,
            Subtotal = order.Subtotal,
            TaxAmount = order.TaxAmount,
            Total = order.Total,
            ReceivedAtUtc = order.ReceivedAtUtc,
            ReceivedBy = order.ReceivedBy,
            Details = order.Details
                .OrderBy(detail => detail.Id)
                .Select(detail => new PurchaseOrderDetailInputViewModel
                {
                    Id = detail.Id,
                    ProductId = detail.ProductId,
                    Qty = detail.Qty,
                    Price = detail.Price,
                    DiscountPercent = detail.DiscountPercent
                })
                .ToList()
        };

    private ViewResult AccessDenied() =>
        new()
        {
            ViewName = "/Views/Shared/AccessDenied.cshtml",
            StatusCode = StatusCodes.Status403Forbidden
        };
}
