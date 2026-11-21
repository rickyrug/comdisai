using System.Diagnostics;
using System.Globalization;
using ComdisAI.Authorization;
using ComdisAI.Models;
using ComdisAI.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComdisAI.Controllers;

public class HomeController(IActionPermissionService permissionService) : Controller
{
    [RequireAction(AuthorizationActionCodes.ViewHome)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var canCreateSalesOrderTask = permissionService.HasActionAsync(
            User, AuthorizationActionCodes.CreateSalesOrder, cancellationToken);
        var canCreatePurchaseOrderTask = permissionService.HasActionAsync(
            User, AuthorizationActionCodes.CreatePurchaseOrder, cancellationToken);

        await Task.WhenAll(canCreateSalesOrderTask, canCreatePurchaseOrderTask);

        return View(BuildDashboard(
            canCreateSalesOrderTask.Result,
            canCreatePurchaseOrderTask.Result));
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

    private static HomeDashboardViewModel BuildDashboard(
        bool canCreateSalesOrder,
        bool canCreatePurchaseOrder)
    {
        var today = DateTime.Today;
        var mexicoCulture = CultureInfo.GetCultureInfo("es-MX");

        return new HomeDashboardViewModel
        {
            MonthlySales = new DashboardKpiViewModel
            {
                Label = "Ventas del mes",
                Value = 1_284_650m.ToString("C0", mexicoCulture),
                Detail = "+12.8% vs. mes anterior",
                SupportingText = $"Meta mensual: {1_750_000m.ToString("C0", mexicoCulture)}",
                ProgressPercent = 73.4m,
                IconClass = "bi-graph-up-arrow"
            },
            SalesOrdersToFulfill = new DashboardKpiViewModel
            {
                Label = "Pedidos por surtir",
                Value = "8",
                Detail = "4 programados para hoy",
                SupportingText = $"Cartera abierta: {742_380m.ToString("C0", mexicoCulture)}",
                ProgressPercent = 50m,
                IconClass = "bi-box-seam"
            },
            PurchasesInTransit = new DashboardKpiViewModel
            {
                Label = "Compras en tránsito",
                Value = "5",
                Detail = $"{486_920m.ToString("C0", mexicoCulture)} en tránsito",
                SupportingText = "2 arribos previstos esta semana",
                ProgressPercent = 60m,
                IconClass = "bi-truck"
            },
            DeliveriesToday = new DashboardKpiViewModel
            {
                Label = "Entregas hoy",
                Value = "6",
                Detail = "2 entregadas · 4 pendientes",
                SupportingText = "Próxima ruta: 11:30",
                ProgressPercent = 33.3m,
                IconClass = "bi-calendar-check"
            },
            CanCreateSalesOrder = canCreateSalesOrder,
            CanCreatePurchaseOrder = canCreatePurchaseOrder,
            ActiveSalesOrders =
            [
                SalesOrder("2026-00184", "Distribuidora La Fragua, S.A. de C.V.", "DFR1503208A1", today.AddDays(-2), today, 86_240m, "Aprobado", "text-bg-success"),
                SalesOrder("2026-00183", "Abastecedora del Centro", "ACE901107P42", today.AddDays(-2), today, 54_760m, "Preparando", "text-bg-warning"),
                SalesOrder("2026-00180", "Grupo Comercial Bajío, S.A.P.I.", "GCB120615KJ8", today.AddDays(-3), today.AddDays(1), 123_980m, "Aprobado", "text-bg-success"),
                SalesOrder("2026-00178", "Ferreterías Unión Norte", "FUN0802147L5", today.AddDays(-4), today.AddDays(1), 38_450m, "Por facturar", "text-bg-primary"),
                SalesOrder("2026-00175", "Servicios Industriales Pacífico", "SIP190911M10", today.AddDays(-5), today.AddDays(2), 97_630m, "En ruta", "text-bg-info")
            ],
            PurchaseOrders =
            [
                PurchaseOrder("2026-00067", "Empaques del Valle, S.A. de C.V.", today.AddDays(1), 148_600m, "En tránsito", "text-bg-info"),
                PurchaseOrder("2026-00065", "Suministros Técnicos Monterrey", today.AddDays(2), 96_840m, "Aprobado", "text-bg-success"),
                PurchaseOrder("2026-00063", "Materiales Guadalajara, S.A.", today.AddDays(4), 174_320m, "En tránsito", "text-bg-info"),
                PurchaseOrder("2026-00061", "Componentes Industriales del Sur", today.AddDays(6), 67_160m, "Confirmado", "text-bg-primary")
            ],
            TopProducts =
            [
                TopProduct("A0142", "Caja corrugada doble 40 × 30 cm", "Empaque", 486, 100m),
                TopProduct("B0087", "Guante nitrilo industrial talla L", "Seguridad", 392, 80.7m),
                TopProduct("C0218", "Aceite lubricante sintético 20 L", "Mantenimiento", 318, 65.4m),
                TopProduct("D0056", "Etiqueta térmica 100 × 150 mm", "Consumibles", 271, 55.8m),
                TopProduct("A0094", "Película estirable 500 mm", "Empaque", 229, 47.1m)
            ],
            ProductSearchResults =
            [
                ProductSearch("A0142", "Caja corrugada doble 40 × 30 cm", "Empaque"),
                ProductSearch("B0087", "Guante nitrilo industrial talla L", "Seguridad"),
                ProductSearch("C0218", "Aceite lubricante sintético 20 L", "Mantenimiento"),
                ProductSearch("D0056", "Etiqueta térmica 100 × 150 mm", "Consumibles"),
                ProductSearch("A0094", "Película estirable 500 mm", "Empaque"),
                ProductSearch("E0031", "Tarima plástica reforzada", "Logística")
            ],
            CustomerSearchResults =
            [
                CustomerSearch("Distribuidora La Fragua, S.A. de C.V.", "DFR1503208A1", "Querétaro"),
                CustomerSearch("Abastecedora del Centro", "ACE901107P42", "León"),
                CustomerSearch("Grupo Comercial Bajío, S.A.P.I.", "GCB120615KJ8", "Guadalajara"),
                CustomerSearch("Ferreterías Unión Norte", "FUN0802147L5", "Monterrey"),
                CustomerSearch("Servicios Industriales Pacífico", "SIP190911M10", "Tijuana")
            ]
        };
    }

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
        int unitsSold,
        decimal rotationPercent) =>
        new()
        {
            Code = code,
            Name = name,
            Category = category,
            UnitsSold = unitsSold,
            RotationPercent = rotationPercent
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
        string city) =>
        new()
        {
            Name = name,
            Rfc = rfc,
            City = city
        };
}
