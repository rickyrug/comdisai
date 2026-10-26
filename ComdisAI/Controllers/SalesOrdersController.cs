using System.Data;
using System.Globalization;
using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.Services;
using ComdisAI.ViewModels.SalesOrders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class SalesOrdersController(
    IUnitOfWork unitOfWork,
    AppDbContext dbContext,
    IActionPermissionService permissionService,
    SalesOrderPdfService pdfService) : Controller
{
    private const string SalesOrderEntity = "SalesOrder";
    private const string DraftStatus = "Draft";
    private const string ApprovedStatus = "Approved";
    private const string DeliveredStatus = "Delivered";
    private const string CancelledStatus = "Cancelled";

    [RequireAction(AuthorizationActionCodes.ViewSalesOrder)]
    public async Task<IActionResult> Index(
        string? q,
        int? statusId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SalesOrderHeaders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.Status)
            .Include(order => order.Details)
            .AsQueryable();
        var term = q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(order =>
                order.OrderNumber.Contains(term) ||
                order.Customer.Name.Contains(term) ||
                order.Status.Name.Contains(term));
        }
        if (statusId is not null)
            query = query.Where(order => order.StatusId == statusId);

        var orders = await query
            .OrderByDescending(order => order.OrderNumber)
            .ToListAsync(cancellationToken);
        var statuses = await dbContext.Statuses
            .AsNoTracking()
            .Where(status => EF.Property<string>(status, "NormalizedEntity") == SalesOrderEntity.ToUpperInvariant())
            .OrderBy(status => status.Name)
            .ToListAsync(cancellationToken);
        ViewData["Statuses"] = new SelectList(statuses, nameof(Status.Id), nameof(Status.Name), statusId);
        ViewData["Query"] = term;
        ViewData["StatusId"] = statusId;
        ViewData["CanApprove"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.ApproveSalesOrder, cancellationToken);
        ViewData["CanDeliver"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.DeliverSalesOrder, cancellationToken);
        ViewData["CanPrint"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.PrintSalesOrder, cancellationToken);
        ViewData["CanCreate"] = await permissionService.HasActionAsync(
            User, AuthorizationActionCodes.CreateSalesOrder, cancellationToken);
        return View(orders);
    }

    [RequireAction(AuthorizationActionCodes.ViewSalesOrder)]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();

        var hasDetails = order.Details.Count > 0;
        var isDelivered = IsStatus(order.Status, DeliveredStatus);
        ViewData["CanEdit"] = !IsStatus(order.Status, CancelledStatus) && await permissionService.HasActionAsync(
            User,
            isDelivered
                ? AuthorizationActionCodes.EditDeliveredSalesOrder
                : AuthorizationActionCodes.EditSalesOrder,
            cancellationToken);
        ViewData["CanApprove"] = hasDetails &&
            IsStatus(order.Status, DraftStatus) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.ApproveSalesOrder, cancellationToken);
        ViewData["CanDeliver"] = IsStatus(order.Status, ApprovedStatus) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.DeliverSalesOrder, cancellationToken);
        ViewData["CanCancel"] = (IsStatus(order.Status, DraftStatus) || IsStatus(order.Status, ApprovedStatus)) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.CancelSalesOrder, cancellationToken);
        ViewData["CanPrint"] = hasDetails &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.PrintSalesOrder, cancellationToken);
        return View(order);
    }

    [HttpGet]
    [RequireAction(AuthorizationActionCodes.CreateSalesOrder)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new SalesOrderHeaderInputViewModel();
        await PopulateHeaderOptionsAsync(model, cancellationToken);
        AddMissingStatusErrors(await GetRequiredStatusesAsync(cancellationToken));
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CreateSalesOrder)]
    public async Task<IActionResult> Create(
        SalesOrderHeaderInputViewModel input,
        CancellationToken cancellationToken)
    {
        ValidateHeaderInput(input);
        var statuses = await GetRequiredStatusesAsync(cancellationToken);
        AddMissingStatusErrors(statuses);
        var customer = await dbContext.Customers
            .SingleOrDefaultAsync(row => row.Id == input.CustomerId, cancellationToken);
        if (customer is null)
            ModelState.AddModelError(nameof(input.CustomerId), "Select an active customer.");
        var tax = await dbContext.Taxes
            .SingleOrDefaultAsync(row => row.Id == input.TaxId, cancellationToken);
        ValidateTax(tax, nameof(input.TaxId));

        if (!ModelState.IsValid || customer is null || tax is null || statuses is null)
        {
            await PopulateHeaderOptionsAsync(input, cancellationToken);
            return View(input);
        }

        var year = DateTime.UtcNow.Year;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existingNumbers = await dbContext.SalesOrderHeaders
            .AsNoTracking()
            .Where(order => order.OrderNumber.StartsWith(
                year.ToString("0000", CultureInfo.InvariantCulture) + "-"))
            .Select(order => order.OrderNumber)
            .ToListAsync(cancellationToken);
        var header = new SalesOrderHeader
        {
            OrderNumber = SalesOrderNumberGenerator.NextNumber(year, existingNumbers),
            CustomerId = customer.Id,
            DeliveryAddress = input.DeliveryAddress.Trim(),
            DeliveryDate = input.DeliveryDate.Date,
            TaxId = tax.Id,
            StatusId = statuses[DraftStatus].Id,
            DiscountPercent1 = input.DiscountPercent1,
            DiscountPercent2 = input.DiscountPercent2,
            DiscountPercent3 = input.DiscountPercent3,
            Tax = tax
        };
        SalesOrderPricing.Recalculate(header);
        await unitOfWork.Repository<SalesOrderHeader>().AddAsync(header, cancellationToken);
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
        SalesOrderEditViewModel input,
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
        var customer = await dbContext.Customers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.Id == input.CustomerId, cancellationToken);
        if (customer is null || (customer.IsDeleted && customer.Id != order.CustomerId))
            ModelState.AddModelError(nameof(input.CustomerId), "Select an active customer.");
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
            ModelState.AddModelError(nameof(input.Details), "One or more submitted sales order lines are invalid.");
        }

        if (!ModelState.IsValid || customer is null || tax is null || statuses is null)
        {
            input.OrderNumber = order.OrderNumber;
            input.StatusName = order.Status.Name;
            await PopulateEditOptionsAsync(input, cancellationToken);
            await PopulateStatusActionsAsync(order, cancellationToken);
            return View(input);
        }

        order.CustomerId = customer.Id;
        order.DeliveryAddress = input.DeliveryAddress.Trim();
        order.DeliveryDate = input.DeliveryDate.Date;
        order.TaxId = tax.Id;
        order.Tax = tax;
        order.DiscountPercent1 = input.DiscountPercent1;
        order.DiscountPercent2 = input.DiscountPercent2;
        order.DiscountPercent3 = input.DiscountPercent3;

        var retainedIds = submittedIds.ToHashSet();
        foreach (var existingDetail in order.Details.Where(detail => !retainedIds.Contains(detail.Id)).ToList())
        {
            unitOfWork.Repository<SalesOrderDetail>().Delete(existingDetail);
            order.Details.Remove(existingDetail);
        }

        foreach (var detailInput in input.Details)
        {
            var detail = detailInput.Id.HasValue
                ? existingDetailsById[detailInput.Id.Value]
                : new SalesOrderDetail();
            detail.ProductId = detailInput.ProductId;
            detail.Qty = detailInput.Qty;
            detail.Price = detailInput.Price;
            detail.DiscountPercent = detailInput.DiscountPercent;

            if (!detailInput.Id.HasValue)
                order.Details.Add(detail);
        }

        if (!IsStatus(order.Status, DeliveredStatus))
        {
            order.StatusId = statuses[DraftStatus].Id;
            order.Status = statuses[DraftStatus];
        }

        try
        {
            SalesOrderPricing.Recalculate(order);
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
        TempData["Success"] = "Sales order saved. Any previous approval was reset to Draft.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.ApproveSalesOrder)]
    public async Task<IActionResult> Approve(int id, string? returnTo, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (!IsStatus(order.Status, DraftStatus) || order.Details.Count == 0)
        {
            TempData["Error"] = "Only a Draft sales order with at least one line can be approved.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        var statuses = await GetRequiredStatusesAsync(cancellationToken);
        if (statuses is null)
        {
            TempData["Error"] = "SalesOrder statuses are not configured. Add Draft, Approved, and Delivered statuses.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        order.StatusId = statuses[ApprovedStatus].Id;
        order.Status = statuses[ApprovedStatus];
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Sales order approved.";
        return RedirectAfterStatusChange(id, returnTo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.DeliverSalesOrder)]
    public async Task<IActionResult> Deliver(int id, string? returnTo, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (!IsStatus(order.Status, ApprovedStatus) || order.Details.Count == 0)
        {
            TempData["Error"] = "Only an Approved sales order with at least one line can be marked as Delivered.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        var statuses = await GetRequiredStatusesAsync(cancellationToken);
        if (statuses is null)
        {
            TempData["Error"] = "SalesOrder statuses are not configured. Add Draft, Approved, and Delivered statuses.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        order.StatusId = statuses[DeliveredStatus].Id;
        order.Status = statuses[DeliveredStatus];
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Sales order marked as Delivered.";
        return RedirectAfterStatusChange(id, returnTo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequireAction(AuthorizationActionCodes.CancelSalesOrder)]
    public async Task<IActionResult> Cancel(int id, string? returnTo, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (!IsStatus(order.Status, DraftStatus) && !IsStatus(order.Status, ApprovedStatus))
        {
            TempData["Error"] = "Only a Draft or Approved sales order can be cancelled.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        var cancelled = await dbContext.Statuses
            .FirstOrDefaultAsync(status =>
                EF.Property<string>(status, "NormalizedEntity") == SalesOrderEntity.ToUpperInvariant() &&
                EF.Property<string>(status, "NormalizedName") == CancelledStatus.ToUpperInvariant(),
                cancellationToken);
        if (cancelled is null)
        {
            TempData["Error"] = "SalesOrder status 'Cancelled' is not configured. Add it in Statuses first.";
            return RedirectAfterStatusChange(id, returnTo);
        }

        order.StatusId = cancelled.Id;
        order.Status = cancelled;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Sales order cancelled.";
        return RedirectAfterStatusChange(id, returnTo);
    }

    [RequireAction(AuthorizationActionCodes.PrintSalesOrder)]
    public async Task<IActionResult> Print(int id, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(id, cancellationToken);
        if (order is null)
            return NotFound();
        if (order.Details.Count == 0)
        {
            TempData["Error"] = "A sales order must have at least one line before it can be printed.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var pdf = pdfService.Create(order);
        return File(pdf, "application/pdf", $"SalesOrder-{order.OrderNumber}.pdf");
    }

    private IActionResult RedirectAfterStatusChange(int id, string? returnTo) =>
        returnTo == "index"
            ? RedirectToAction(nameof(Index))
            : RedirectToAction(nameof(Details), new { id });

    private async Task<SalesOrderHeader?> GetOrderAsync(int id, CancellationToken cancellationToken) =>
        await dbContext.SalesOrderHeaders
            .IgnoreQueryFilters()
            .Include(order => order.Customer)
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
        SalesOrderHeader order,
        CancellationToken cancellationToken) =>
        !IsStatus(order.Status, CancelledStatus) &&
        await permissionService.HasActionAsync(
            User,
            IsStatus(order.Status, DeliveredStatus)
                ? AuthorizationActionCodes.EditDeliveredSalesOrder
                : AuthorizationActionCodes.EditSalesOrder,
            cancellationToken);

    private async Task PopulateHeaderOptionsAsync(
        SalesOrderHeaderInputViewModel model,
        CancellationToken cancellationToken)
    {
        model.Customers = await dbContext.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.Name)
            .Select(customer => new SelectListItem(customer.Name, customer.Id.ToString(CultureInfo.InvariantCulture)))
            .ToListAsync(cancellationToken);
        var addresses = await dbContext.Customers
            .AsNoTracking()
            .Select(customer => new { customer.Id, customer.Address })
            .ToListAsync(cancellationToken);
        model.CustomerAddresses = addresses.ToDictionary(
            row => row.Id.ToString(CultureInfo.InvariantCulture),
            row => row.Address);
        model.CustomerDefaultTaxes = (await dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.DefaultTaxId != null && customer.DefaultTax!.IsDeleted == false)
            .Select(customer => new { customer.Id, TaxId = customer.DefaultTaxId!.Value })
            .ToListAsync(cancellationToken))
            .ToDictionary(
                row => row.Id.ToString(CultureInfo.InvariantCulture),
                row => row.TaxId.ToString(CultureInfo.InvariantCulture));
        model.Taxes = await dbContext.Taxes
            .AsNoTracking()
            .OrderBy(tax => tax.Name)
            .Select(tax => new SelectListItem(
                tax.Name + " (" + (tax.Value * 100m).ToString("0.####", CultureInfo.InvariantCulture) + "%)",
                tax.Id.ToString(CultureInfo.InvariantCulture)))
            .ToListAsync(cancellationToken);
    }

    private async Task PopulateStatusActionsAsync(SalesOrderHeader order, CancellationToken cancellationToken)
    {
        ViewData["CanApprove"] = order.Details.Count > 0 &&
            IsStatus(order.Status, DraftStatus) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.ApproveSalesOrder, cancellationToken);
        ViewData["CanDeliver"] = IsStatus(order.Status, ApprovedStatus) &&
            await permissionService.HasActionAsync(User, AuthorizationActionCodes.DeliverSalesOrder, cancellationToken);
    }

    private async Task PopulateEditOptionsAsync(
        SalesOrderEditViewModel model,
        CancellationToken cancellationToken)
    {
        var existingReferences = await dbContext.SalesOrderHeaders
            .IgnoreQueryFilters()
            .Where(order => order.Id == model.Id)
            .Select(order => new { order.CustomerId, order.TaxId })
            .SingleAsync(cancellationToken);
        var customers = await dbContext.Customers
            .IgnoreQueryFilters()
            .Where(customer => !customer.IsDeleted || customer.Id == existingReferences.CustomerId)
            .OrderBy(customer => customer.Name)
            .ToListAsync(cancellationToken);
        model.Customers = customers
            .Select(customer => new SelectListItem(
                customer.Name + (customer.IsDeleted ? " (inactive)" : string.Empty),
                customer.Id.ToString(CultureInfo.InvariantCulture)))
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
        var existingProductIds = await dbContext.SalesOrderDetails
            .IgnoreQueryFilters()
            .Where(detail => detail.SalesOrderHeaderId == model.Id)
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
        var requiredNames = new[] { DraftStatus, ApprovedStatus, DeliveredStatus };
        var normalizedNames = requiredNames.Select(name => name.ToUpperInvariant()).ToArray();
        var statuses = await dbContext.Statuses
            .Where(status =>
                EF.Property<string>(status, "NormalizedEntity") == SalesOrderEntity.ToUpperInvariant() &&
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
            "SalesOrder status configuration is incomplete. Add active Draft, Approved, and Delivered statuses.");
    }

    private void ValidateHeaderInput(SalesOrderHeaderInputViewModel input)
    {
        input.DeliveryAddress = input.DeliveryAddress?.Trim() ?? string.Empty;
        ModelState.Remove(nameof(input.DeliveryAddress));
        TryValidateModel(input);
        if (input.DeliveryDate == default)
            ModelState.AddModelError(nameof(input.DeliveryDate), "A delivery date is required.");
        ValidatePrecision(input.DiscountPercent1, nameof(input.DiscountPercent1));
        ValidatePrecision(input.DiscountPercent2, nameof(input.DiscountPercent2));
        ValidatePrecision(input.DiscountPercent3, nameof(input.DiscountPercent3));
    }

    private void ValidateDetailsInput(IReadOnlyList<SalesOrderDetailInputViewModel> details)
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

    private SalesOrderEditViewModel MapToEditViewModel(SalesOrderHeader order) =>
        new()
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            StatusName = order.Status.Name,
            CustomerId = order.CustomerId,
            DeliveryAddress = order.DeliveryAddress,
            DeliveryDate = order.DeliveryDate,
            TaxId = order.TaxId,
            DiscountPercent1 = order.DiscountPercent1,
            DiscountPercent2 = order.DiscountPercent2,
            DiscountPercent3 = order.DiscountPercent3,
            Subtotal = order.Subtotal,
            TaxAmount = order.TaxAmount,
            Total = order.Total,
            Details = order.Details
                .OrderBy(detail => detail.Id)
                .Select(detail => new SalesOrderDetailInputViewModel
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
