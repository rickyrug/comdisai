using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.ViewModels.PurchaseReports;

public sealed class PurchaseReportViewModel
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public List<int> StatusIds { get; set; } = [];
    public IReadOnlyList<SelectListItem> StatusOptions { get; set; } = [];
    public IReadOnlyList<PurchaseReportRowViewModel> Orders { get; set; } = [];
    public IReadOnlyList<PurchaseReportStatusSummaryViewModel> StatusSummary { get; set; } = [];
    public decimal PeriodTotal { get; set; }
}

public sealed class PurchaseReportRowViewModel
{
    public DateTime SupplyDate { get; init; }
    public string SupplierName { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public decimal Total { get; init; }
}

public sealed class PurchaseReportStatusSummaryViewModel
{
    public string StatusName { get; init; } = string.Empty;
    public int OrderCount { get; init; }
    public decimal Total { get; init; }
}
