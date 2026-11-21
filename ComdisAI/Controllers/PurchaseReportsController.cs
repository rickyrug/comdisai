using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.ViewModels.PurchaseReports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class PurchaseReportsController(AppDbContext dbContext) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewPurchaseReport)]
    public async Task<IActionResult> Index(
        DateOnly? startDate,
        DateOnly? endDate,
        int[]? statusIds,
        CancellationToken cancellationToken)
    {
        if (startDate is null && endDate is null)
        {
            startDate = DateOnly.FromDateTime(DateTime.Today);
            endDate = startDate;
        }

        var statuses = await dbContext.Statuses
            .AsNoTracking()
            .Where(status => EF.Property<string>(status, "NormalizedEntity") == "PURCHASEORDER")
            .OrderBy(status => status.Name)
            .ToListAsync(cancellationToken);
        var model = new PurchaseReportViewModel
        {
            StartDate = startDate,
            EndDate = endDate,
            StatusIds = statusIds?.Distinct().ToList() ?? [],
            StatusOptions = statuses
                .Select(status => new SelectListItem(
                    status.Name,
                    status.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    statusIds?.Contains(status.Id) == true))
                .ToList()
        };

        if (startDate is null || endDate is null)
        {
            ModelState.AddModelError(string.Empty, "Enter both a start date and an end date.");
            return View(model);
        }

        if (startDate > endDate)
        {
            ModelState.AddModelError(nameof(model.EndDate), "The end date must be on or after the start date.");
            return View(model);
        }

        if (model.StatusIds.Any(statusId => statuses.All(status => status.Id != statusId)))
        {
            ModelState.AddModelError(nameof(model.StatusIds), "Select valid purchase order statuses.");
            return View(model);
        }

        var rangeStart = startDate.Value.ToDateTime(TimeOnly.MinValue);
        var query = dbContext.PurchaseOrderHeaders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order => order.SupplyDate >= rangeStart);
        if (endDate.Value < DateOnly.MaxValue)
        {
            var rangeEndExclusive = endDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(order => order.SupplyDate < rangeEndExclusive);
        }
        if (model.StatusIds.Count > 0)
            query = query.Where(order => model.StatusIds.Contains(order.StatusId));

        model.Orders = await query
            .OrderBy(order => order.SupplyDate)
            .ThenBy(order => order.OrderNumber)
            .Select(order => new PurchaseReportRowViewModel
            {
                SupplyDate = order.SupplyDate,
                SupplierName = order.Supplier.Name,
                OrderNumber = order.OrderNumber,
                StatusName = order.Status.Name,
                Total = order.Total
            })
            .ToListAsync(cancellationToken);
        model.PeriodTotal = model.Orders.Sum(order => order.Total);
        model.StatusSummary = model.Orders
            .GroupBy(order => order.StatusName)
            .Select(group => new PurchaseReportStatusSummaryViewModel
            {
                StatusName = group.Key,
                OrderCount = group.Count(),
                Total = group.Sum(order => order.Total)
            })
            .OrderBy(summary => summary.StatusName)
            .ToList();
        return View(model);
    }
}
