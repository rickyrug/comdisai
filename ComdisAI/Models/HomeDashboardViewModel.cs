namespace ComdisAI.Models;

public sealed class HomeDashboardViewModel
{
    public DashboardKpiViewModel MonthlySales { get; init; } = new();

    public DashboardKpiViewModel SalesOrdersToFulfill { get; init; } = new();

    public DashboardKpiViewModel PurchasesInTransit { get; init; } = new();

    public DashboardKpiViewModel DeliveriesToday { get; init; } = new();

    public bool CanCreateSalesOrder { get; init; }

    public bool CanCreatePurchaseOrder { get; init; }

    public IReadOnlyList<DashboardSalesOrderRowViewModel> ActiveSalesOrders { get; init; } = [];

    public IReadOnlyList<DashboardPurchaseOrderRowViewModel> PurchaseOrders { get; init; } = [];

    public IReadOnlyList<DashboardTopProductViewModel> TopProducts { get; init; } = [];

    public IReadOnlyList<DashboardProductSearchViewModel> ProductSearchResults { get; init; } = [];

    public IReadOnlyList<DashboardCustomerSearchViewModel> CustomerSearchResults { get; init; } = [];
}

public sealed class DashboardKpiViewModel
{
    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string SupportingText { get; init; } = string.Empty;

    public decimal ProgressPercent { get; init; }

    public string IconClass { get; init; } = string.Empty;
}

public sealed class DashboardSalesOrderRowViewModel
{
    public string OrderNumber { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerRfc { get; init; } = string.Empty;

    public DateTime OrderDate { get; init; }

    public DateTime DeliveryDate { get; init; }

    public decimal TotalMxn { get; init; }

    public string Status { get; init; } = string.Empty;

    public string BadgeClass { get; init; } = string.Empty;
}

public sealed class DashboardPurchaseOrderRowViewModel
{
    public string OrderNumber { get; init; } = string.Empty;

    public string SupplierName { get; init; } = string.Empty;

    public DateTime ExpectedDate { get; init; }

    public decimal TotalMxn { get; init; }

    public string Status { get; init; } = string.Empty;

    public string BadgeClass { get; init; } = string.Empty;
}

public sealed class DashboardTopProductViewModel
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public int UnitsSold { get; init; }

    public decimal RotationPercent { get; init; }
}

public sealed class DashboardProductSearchViewModel
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;
}

public sealed class DashboardCustomerSearchViewModel
{
    public string Name { get; init; } = string.Empty;

    public string Rfc { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;
}
