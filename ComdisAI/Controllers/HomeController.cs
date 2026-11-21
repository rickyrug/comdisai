using System.Diagnostics;
using System.Globalization;
using ComdisAI.Authorization;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Controllers;

public class HomeController(
    IActionPermissionService permissionService,
    AppDbContext dbContext) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewHome)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var canCreateSalesOrderTask = permissionService.HasActionAsync(
            User, AuthorizationActionCodes.CreateSalesOrder, cancellationToken);
        var canCreatePurchaseOrderTask = permissionService.HasActionAsync(
            User, AuthorizationActionCodes.CreatePurchaseOrder, cancellationToken);

        await Task.WhenAll(canCreateSalesOrderTask, canCreatePurchaseOrderTask);

        return View(await BuildDashboardAsync(
            canCreateSalesOrderTask.Result,
            canCreatePurchaseOrderTask.Result,
            cancellationToken));
    }

    [RequireAction(AuthorizationActionCodes.ViewPrivacy)]
    public IActionResult Privacy()
    {
        return View();
    }

    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private async Task<HomeDashboardViewModel> BuildDashboardAsync(
        bool canCreateSalesOrder,
        bool canCreatePurchaseOrder,
        CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var previousMonthStart = monthStart.AddMonths(-1);
        var mexicoCulture = CultureInfo.GetCultureInfo("es-MX");

        var periodSales = await dbContext.SalesOrderHeaders
            .AsNoTracking()
            .Include(order => order.Status)
            .Where(order => order.DeliveryDate >= previousMonthStart && order.DeliveryDate < nextMonthStart)
            .Where(order =>
                EF.Property<string>(order.Status, "NormalizedName") == "APPROVED" ||
                EF.Property<string>(order.Status, "NormalizedName") == "DELIVERED" ||
                EF.Property<string>(order.Status, "NormalizedName") == "PAID")
            .ToListAsync(cancellationToken);
        var monthlySales = periodSales.Where(order => order.DeliveryDate >= monthStart).ToList();
        var previousMonthSales = periodSales.Where(order => order.DeliveryDate < monthStart).ToList();
        var monthlySalesTotal = monthlySales.Sum(order => order.Total);
        var previousMonthSalesTotal = previousMonthSales.Sum(order => order.Total);

        var salesToFulfill = await dbContext.SalesOrderHeaders
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.Status)
            .Where(order =>
                EF.Property<string>(order.Status, "NormalizedName") == "APPROVED" &&
                order.IsDelivered != true)
            .OrderBy(order => order.DeliveryDate)
            .ThenBy(order => order.OrderNumber)
            .ToListAsync(cancellationToken);
        var purchasesInTransit = await dbContext.PurchaseOrderHeaders
            .AsNoTracking()
            .Include(order => order.Supplier)
            .Include(order => order.Status)
            .Where(order =>
                EF.Property<string>(order.Status, "NormalizedName") == "APPROVED" &&
                order.ReceivedAtUtc == null)
            .OrderBy(order => order.SupplyDate)
            .ThenBy(order => order.OrderNumber)
            .ToListAsync(cancellationToken);
        var deliveriesToday = await dbContext.SalesOrderHeaders
            .AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.Status)
            .Where(order => order.DeliveryDate >= today && order.DeliveryDate < tomorrow)
            .Where(order =>
                EF.Property<string>(order.Status, "NormalizedName") == "APPROVED" ||
                EF.Property<string>(order.Status, "NormalizedName") == "DELIVERED" ||
                EF.Property<string>(order.Status, "NormalizedName") == "PAID")
            .OrderBy(order => order.OrderNumber)
            .ToListAsync(cancellationToken);

        var productMovements = await dbContext.SalesOrderDetails
            .AsNoTracking()
            .Include(detail => detail.Product)
                .ThenInclude(product => product.ProductCategory)
            .Include(detail => detail.SalesOrderHeader)
                .ThenInclude(order => order.Status)
            .Where(detail =>
                detail.SalesOrderHeader.DeliveryDate >= monthStart &&
                detail.SalesOrderHeader.DeliveryDate < nextMonthStart)
            .Where(detail =>
                EF.Property<string>(detail.SalesOrderHeader.Status, "NormalizedName") == "DELIVERED" ||
                EF.Property<string>(detail.SalesOrderHeader.Status, "NormalizedName") == "PAID")
            .ToListAsync(cancellationToken);
        var products = await dbContext.Products
            .AsNoTracking()
            .Include(product => product.ProductCategory)
            .OrderBy(product => product.Code)
            .ToListAsync(cancellationToken);
        var customers = await dbContext.Customers
            .AsNoTracking()
            .OrderBy(customer => customer.Name)
            .ToListAsync(cancellationToken);

        var monthlySalesChange = previousMonthSalesTotal > 0m
            ? (monthlySalesTotal - previousMonthSalesTotal) / previousMonthSalesTotal * 100m
            : (decimal?)null;
        var salesToFulfillToday = salesToFulfill.Count(order => order.DeliveryDate.Date == today);
        var purchasesThisWeek = purchasesInTransit.Count(order =>
            order.SupplyDate >= today && order.SupplyDate < today.AddDays(8));
        var deliveredToday = deliveriesToday.Count(order =>
            order.IsDelivered == true || IsStatus(order.Status, "Delivered"));
        var pendingToday = deliveriesToday.Count - deliveredToday;
        var topProducts = productMovements
            .GroupBy(detail => detail.ProductId)
            .Select(group =>
            {
                var product = group.First().Product;
                return new
                {
                    product.Code,
                    product.Name,
                    Category = product.ProductCategory.Name,
                    UnitsSold = group.Sum(detail => detail.Qty)
                };
            })
            .OrderByDescending(product => product.UnitsSold)
            .Take(5)
            .ToList();
        var maxUnitsSold = topProducts.FirstOrDefault()?.UnitsSold ?? 0m;

        return new HomeDashboardViewModel
        {
            MonthlySales = new DashboardKpiViewModel
            {
                Label = "Ventas del mes",
                Value = monthlySalesTotal.ToString("C0", mexicoCulture),
                Detail = monthlySalesChange is null
                    ? "Sin referencia del mes anterior"
                    : $"{monthlySalesChange:+0.0;-0.0;0.0}% vs. mes anterior",
                SupportingText = $"{monthlySales.Count} pedidos · mes anterior {previousMonthSalesTotal.ToString("C0", mexicoCulture)}",
                ProgressPercent = CalculateProgress(
                    monthlySalesTotal,
                    Math.Max(monthlySalesTotal, previousMonthSalesTotal)),
                IconClass = "bi-graph-up-arrow"
            },
            SalesOrdersToFulfill = new DashboardKpiViewModel
            {
                Label = "Pedidos por surtir",
                Value = salesToFulfill.Count.ToString(CultureInfo.InvariantCulture),
                Detail = $"{salesToFulfillToday} programados para hoy",
                SupportingText = $"Cartera abierta: {salesToFulfill.Sum(order => order.Total).ToString("C0", mexicoCulture)}",
                ProgressPercent = CalculateProgress(salesToFulfillToday, salesToFulfill.Count),
                IconClass = "bi-box-seam"
            },
            PurchasesInTransit = new DashboardKpiViewModel
            {
                Label = "Compras en tránsito",
                Value = purchasesInTransit.Count.ToString(CultureInfo.InvariantCulture),
                Detail = $"{purchasesInTransit.Sum(order => order.Total).ToString("C0", mexicoCulture)} en tránsito",
                SupportingText = $"{purchasesThisWeek} arribos previstos esta semana",
                ProgressPercent = CalculateProgress(purchasesThisWeek, purchasesInTransit.Count),
                IconClass = "bi-truck"
            },
            DeliveriesToday = new DashboardKpiViewModel
            {
                Label = "Entregas hoy",
                Value = deliveriesToday.Count.ToString(CultureInfo.InvariantCulture),
                Detail = $"{deliveredToday} entregadas · {pendingToday} pendientes",
                SupportingText = $"Valor programado: {deliveriesToday.Sum(order => order.Total).ToString("C0", mexicoCulture)}",
                ProgressPercent = CalculateProgress(deliveredToday, deliveriesToday.Count),
                IconClass = "bi-calendar-check"
            },
            CanCreateSalesOrder = canCreateSalesOrder,
            CanCreatePurchaseOrder = canCreatePurchaseOrder,
            ActiveSalesOrders = salesToFulfill
                .Select(order => SalesOrder(
                    order.OrderNumber,
                    order.Customer.Name,
                    order.Customer.RFC,
                    order.CreatedAtUtc,
                    order.DeliveryDate,
                    order.Total,
                    order.Status.Name,
                    GetBadgeClass(order.Status.Name)))
                .ToList(),
            PurchaseOrders = purchasesInTransit
                .Select(order => PurchaseOrder(
                    order.OrderNumber,
                    order.Supplier.Name,
                    order.SupplyDate,
                    order.Total,
                    order.Status.Name,
                    GetBadgeClass(order.Status.Name)))
                .ToList(),
            TopProducts = topProducts
                .Select(product => TopProduct(
                    product.Code,
                    product.Name,
                    product.Category,
                    product.UnitsSold,
                    CalculateProgress(product.UnitsSold, maxUnitsSold)))
                .ToList(),
            ProductSearchResults = products
                .Select(product => ProductSearch(
                    product.Code,
                    product.Name,
                    product.ProductCategory.Name))
                .ToList(),
            CustomerSearchResults = customers
                .Select(customer => CustomerSearch(
                    customer.Name,
                    customer.RFC,
                    customer.Address))
                .ToList(),
            OperationalAlerts =
            [
                OperationalAlert(
                    $"{salesToFulfill.Count} pedidos pendientes de surtir",
                    salesToFulfillToday > 0
                        ? $"{salesToFulfillToday} tienen entrega programada hoy."
                        : "Sin entregas pendientes programadas para hoy.",
                    "bi-box-seam",
                    "text-warning"),
                OperationalAlert(
                    $"{purchasesThisWeek} arribos esta semana",
                    $"{purchasesInTransit.Count} compras permanecen abiertas.",
                    "bi-truck",
                    "text-info"),
                OperationalAlert(
                    $"{products.Count} productos activos",
                    $"{customers.Count} clientes disponibles para venta.",
                    "bi-box",
                    "text-success")
            ]
        };
    }

    private static DashboardOperationalAlertViewModel OperationalAlert(
        string title,
        string detail,
        string iconClass,
        string colorClass) =>
        new()
        {
            Title = title,
            Detail = detail,
            IconClass = iconClass,
            ColorClass = colorClass
        };

    private static DashboardSalesOrderRowViewModel SalesOrder(
        string orderNumber,
        string customerName,
        string customerRfc,
        DateTime orderDate,
        DateTime deliveryDate,
        decimal totalMxn,
        string status,
        string badgeClass) =>
        new()
        {
            OrderNumber = orderNumber,
            CustomerName = customerName,
            CustomerRfc = customerRfc,
            OrderDate = orderDate,
            DeliveryDate = deliveryDate,
            TotalMxn = totalMxn,
            Status = status,
            BadgeClass = badgeClass
        };

    private static DashboardPurchaseOrderRowViewModel PurchaseOrder(
        string orderNumber,
        string supplierName,
        DateTime expectedDate,
        decimal totalMxn,
        string status,
        string badgeClass) =>
        new()
        {
            OrderNumber = orderNumber,
            SupplierName = supplierName,
            ExpectedDate = expectedDate,
            TotalMxn = totalMxn,
            Status = status,
            BadgeClass = badgeClass
        };

    private static DashboardTopProductViewModel TopProduct(
        string code,
        string name,
        string category,
        decimal unitsSold,
        decimal rotationPercent) =>
        new()
        {
            Code = code,
            Name = name,
            Category = category,
            UnitsSold = unitsSold,
            RotationPercent = rotationPercent
        };

    private static decimal CalculateProgress(decimal value, decimal total) =>
        total <= 0m ? 0m : Math.Clamp(value / total * 100m, 0m, 100m);

    private static bool IsStatus(Status status, string expected) =>
        string.Equals(status.Name, expected, StringComparison.OrdinalIgnoreCase);

    private static string GetBadgeClass(string status) =>
        status.Trim().ToUpperInvariant() switch
        {
            "APPROVED" or "PAID" or "DELIVERED" or "SUPPLIED" => "text-bg-success",
            "CANCELLED" => "text-bg-danger",
            "DRAFT" => "text-bg-secondary",
            _ => "text-bg-primary"
        };

    private static DashboardProductSearchViewModel ProductSearch(
        string code,
        string name,
        string category) =>
        new()
        {
            Code = code,
            Name = name,
            Category = category
        };

    private static DashboardCustomerSearchViewModel CustomerSearch(
        string name,
        string rfc,
        string location) =>
        new()
        {
            Name = name,
            Rfc = rfc,
            Location = location
        };
}
