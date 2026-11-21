using System.Security.Claims;
using ComdisAI.Authorization;
using ComdisAI.Controllers;
using ComdisAI.Models;
using ComdisAI.Services;
using ComdisAI.ViewModels.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ComdisAI.Tests;

public sealed class HomeDashboardTests
{
    [Fact]
    public async Task Index_returns_a_home_dashboard_view_model()
    {
        var controller = CreateController();

        var result = await controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<HomeDashboardViewModel>(view.Model);
    }

    [Fact]
    public async Task Dashboard_exposes_four_complete_kpi_contracts()
    {
        var model = await GetDashboardAsync();
        var kpis = new[]
        {
            model.MonthlySales,
            model.SalesOrdersToFulfill,
            model.PurchasesInTransit,
            model.DeliveriesToday
        };

        Assert.Equal(
            ["Ventas del mes", "Pedidos por surtir", "Compras en tránsito", "Entregas hoy"],
            kpis.Select(kpi => kpi.Label));
        Assert.All(kpis, kpi =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kpi.Value));
            Assert.False(string.IsNullOrWhiteSpace(kpi.Detail));
            Assert.False(string.IsNullOrWhiteSpace(kpi.SupportingText));
            Assert.InRange(kpi.ProgressPercent, 0m, 100m);
            Assert.StartsWith("bi-", kpi.IconClass);
        });
    }

    [Fact]
    public async Task Dashboard_contains_realistic_non_empty_operational_data()
    {
        var model = await GetDashboardAsync();

        Assert.NotEmpty(model.ActiveSalesOrders);
        Assert.NotEmpty(model.PurchaseOrders);
        Assert.NotEmpty(model.TopProducts);
        Assert.NotEmpty(model.ProductSearchResults);
        Assert.NotEmpty(model.CustomerSearchResults);
        Assert.All(model.ActiveSalesOrders, row => Assert.Equal(10, row.OrderNumber.Length));
        Assert.All(model.PurchaseOrders, row => Assert.Equal(10, row.OrderNumber.Length));
        Assert.All(model.ActiveSalesOrders, row => Assert.True(row.TotalMxn > 0m));
        Assert.All(model.PurchaseOrders, row => Assert.True(row.TotalMxn > 0m));
        Assert.All(model.TopProducts, row =>
        {
            Assert.Equal(5, row.Code.Length);
            Assert.True(row.UnitsSold > 0);
        });
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Dashboard_preserves_create_permission_flags(
        bool canCreateSalesOrder,
        bool canCreatePurchaseOrder)
    {
        var permissions = new List<string>();
        if (canCreateSalesOrder)
            permissions.Add(AuthorizationActionCodes.CreateSalesOrder);
        if (canCreatePurchaseOrder)
            permissions.Add(AuthorizationActionCodes.CreatePurchaseOrder);

        var model = await GetDashboardAsync(permissions);

        Assert.Equal(canCreateSalesOrder, model.CanCreateSalesOrder);
        Assert.Equal(canCreatePurchaseOrder, model.CanCreatePurchaseOrder);
    }

    private static async Task<HomeDashboardViewModel> GetDashboardAsync(
        IEnumerable<string>? actionCodes = null)
    {
        var result = await CreateController(actionCodes ?? []).Index(CancellationToken.None);
        return Assert.IsType<HomeDashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
    }

    private static HomeController CreateController(IEnumerable<string>? actionCodes = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "Mariana López")],
            "test"));
        var httpContext = new DefaultHttpContext { User = principal };

        return new HomeController(new StubActionPermissionService(actionCodes ?? []))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class StubActionPermissionService(IEnumerable<string> actionCodes) : IActionPermissionService
    {
        private readonly HashSet<string> _actionCodes = actionCodes.ToHashSet(StringComparer.Ordinal);

        public Task<IReadOnlyList<RoleSelectionOptionViewModel>> GetActiveRolesAsync(
            int userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleSelectionOptionViewModel>>([]);

        public Task<IReadOnlyList<string>> GetActiveActionCodesAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_actionCodes.ToList());

        public Task<bool> HasActionAsync(
            ClaimsPrincipal principal,
            string actionCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_actionCodes.Contains(actionCode));
    }
}
