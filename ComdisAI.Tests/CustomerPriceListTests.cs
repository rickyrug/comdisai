using ComdisAI.Controllers;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.CustomerPriceLists;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class CustomerPriceListTests
{
    private static async Task<(Microsoft.Data.Sqlite.SqliteConnection Connection, AppDbContext Context, Customer Customer, Product Product)> CreateAsync()
    {
        Batteries.Init();
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());
        await context.Database.EnsureCreatedAsync();
        var uom = new Uom { Name = "Unit" };
        var category = new ProductCategory { Name = "Paper", Prefix = "P" };
        context.AddRange(uom, category);
        await context.SaveChangesAsync();
        var product = new Product { Name = "Item", Code = "1", UomId = uom.Id, ProductCategoryId = category.Id };
        var customer = new Customer { Name = "A", Address = "x", RFC = "R1" };
        context.AddRange(product, customer);
        await context.SaveChangesAsync();
        return (connection, context, customer, product);
    }

    [Fact]
    public async Task Price_is_audited_and_unique_per_customer_and_product()
    {
        var (connection, context, customer, product) = await CreateAsync();
        using var _ = connection;
        using var __ = context;

        context.CustomerPriceLists.Add(new CustomerPriceList
        {
            CustomerId = customer.Id, ProductId = product.Id, Price = 12.5m
        });
        await context.SaveChangesAsync();
        var saved = await context.CustomerPriceLists.AsNoTracking().SingleAsync();
        Assert.Equal(12.5m, saved.Price);
        Assert.Equal("system", saved.CreatedBy);

        context.CustomerPriceLists.Add(new CustomerPriceList
        {
            CustomerId = customer.Id, ProductId = product.Id, Price = 1m
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Controller_creates_edits_rejects_duplicates_and_deletes()
    {
        var (connection, context, customer, product) = await CreateAsync();
        using var _ = connection;
        using var __ = context;
        var controller = new CustomerPriceListController(new UnitOfWork(context));

        var created = await controller.Create(
            customer.Id,
            new CustomerPriceListInputViewModel { ProductId = product.Id, Price = 5m },
            CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(created);
        var item = await context.CustomerPriceLists.AsNoTracking().SingleAsync();
        var initialHistory = await context.CustomerPriceHistories.AsNoTracking().SingleAsync();
        Assert.Equal(customer.Id, initialHistory.CustomerId);
        Assert.Equal(product.Id, initialHistory.ProductId);
        Assert.Null(initialHistory.PreviousPrice);
        Assert.Equal(5m, initialHistory.NewPrice);

        var duplicate = await controller.Create(
            customer.Id,
            new CustomerPriceListInputViewModel { ProductId = product.Id, Price = 6m },
            CancellationToken.None);
        Assert.IsType<ViewResult>(duplicate);
        Assert.False(controller.ModelState.IsValid);

        controller.ModelState.Clear();
        await controller.Edit(
            item.Id,
            new CustomerPriceListInputViewModel { ProductId = product.Id, Price = 7m },
            CancellationToken.None);
        Assert.Equal(7m, (await context.CustomerPriceLists.AsNoTracking().SingleAsync()).Price);
        var changedHistory = await context.CustomerPriceHistories.AsNoTracking()
            .OrderBy(history => history.Id)
            .ToListAsync();
        Assert.Equal(2, changedHistory.Count);
        Assert.Equal(5m, changedHistory[1].PreviousPrice);
        Assert.Equal(7m, changedHistory[1].NewPrice);

        var editResult = Assert.IsType<ViewResult>(await controller.Edit(item.Id, CancellationToken.None));
        var editModel = Assert.IsType<CustomerPriceListInputViewModel>(editResult.Model);
        Assert.Equal(2, editModel.History.Count);

        controller.ModelState.Clear();
        await controller.Edit(
            item.Id,
            new CustomerPriceListInputViewModel { ProductId = product.Id, Price = 7m },
            CancellationToken.None);
        Assert.Equal(2, await context.CustomerPriceHistories.CountAsync());

        var secondProduct = new Product
        {
            Name = "Another item",
            Code = "2",
            UomId = product.UomId,
            ProductCategoryId = product.ProductCategoryId
        };
        context.Products.Add(secondProduct);
        await context.SaveChangesAsync();
        await controller.Edit(
            item.Id,
            new CustomerPriceListInputViewModel { ProductId = secondProduct.Id, Price = 7m },
            CancellationToken.None);
        var reassignmentHistory = await context.CustomerPriceHistories.AsNoTracking()
            .OrderBy(history => history.Id)
            .ToListAsync();
        Assert.Equal(3, reassignmentHistory.Count);
        Assert.Equal(secondProduct.Id, reassignmentHistory[2].ProductId);
        Assert.Null(reassignmentHistory[2].PreviousPrice);

        await controller.Delete(item.Id, CancellationToken.None);
        Assert.Empty(await context.CustomerPriceLists.ToListAsync());
        Assert.Equal(3, await context.CustomerPriceHistories.CountAsync());
    }
}
