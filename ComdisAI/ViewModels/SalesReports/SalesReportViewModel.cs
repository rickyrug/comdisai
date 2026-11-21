using Microsoft.AspNetCore.Mvc.Rendering;

namespace ComdisAI.ViewModels.SalesReports;

public sealed class SalesReportViewModel
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public List<int> StatusIds { get; set; } = [];
    public IReadOnlyList<SelectListItem> StatusOptions { get; set; } = [];
    public IReadOnlyList<SalesReportRowViewModel> Orders { get; set; } = [];
    public IReadOnlyList<SalesReportStatusSummaryViewModel> StatusSummary { get; set; } = [];
    public decimal PeriodTotal { get; set; }
}

public sealed class SalesReportRowViewModel
{
    public DateTime DeliveryDate { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public decimal Total { get; init; }
}

public sealed class SalesReportStatusSummaryViewModel
{
    public string StatusName { get; init; } = string.Empty;
    public int OrderCount { get; init; }
    public decimal Total { get; init; }
}