using ComdisAI.Controllers;
using ComdisAI.Data;
using ComdisAI.Models;
using ComdisAI.Repositories;
using ComdisAI.ViewModels.SupplierPriceLists;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class SupplierPriceListTests
{
    private static async Task<(Microsoft.Data.Sqlite.SqliteConnection Connection, AppDbContext Context, Supplier Supplier, Product Product)> CreateAsync()
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
        var bank = new Bank { Name = "Sample Bank" };
        context.AddRange(uom, category, bank);
        await context.SaveChangesAsync();
        var product = new Product { Name = "Item", Code = "1", UomId = uom.Id, ProductCategoryId = category.Id };
        var supplier = new Supplier
        {
            Name = "Sample Supplier",
            Address = "1 Main Street",
            RFC = "RFC1",
            BankId = bank.Id,
            BankAccount = "123456"
        };
        context.AddRange(product, supplier);
        await context.SaveChangesAsync();
        return (connection, context, supplier, product);
    }

    [Fact]
    public async Task Price_is_audited_and_unique_per_supplier_and_product()
    {
        var (connection, context, supplier, product) = await CreateAsync();
        using var _ = connection;
        using var __ = context;

        context.SupplierPriceLists.Add(new SupplierPriceList
        {
            SupplierId = supplier.Id,
            ProductId = product.Id,
            Price = 12.5m
        });
        await context.SaveChangesAsync();
        var saved = await context.SupplierPriceLists.AsNoTracking().SingleAsync();
        Assert.Equal(12.5m, saved.Price);
        Assert.Equal("system", saved.CreatedBy);
        Assert.NotEqual(default, saved.CreatedAtUtc);

        context.SupplierPriceLists.Add(new SupplierPriceList
        {
            SupplierId = supplier.Id,
            ProductId = product.Id,
            Price = 1m
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Controller_creates_edits_rejects_duplicates_and_deletes()
    {
        var (connection, context, supplier, product) = await CreateAsync();
        using var _ = connection;
        using var __ = context;
        var controller = new SupplierPriceListController(new UnitOfWork(context));

        var created = await controller.Create(
            supplier.Id,
            new SupplierPriceListInputViewModel { ProductId = product.Id, Price = 5m },
            CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(created);
        var item = await context.SupplierPriceLists.AsNoTracking().SingleAsync();

        var duplicate = await controller.Create(
            supplier.Id,
            new SupplierPriceListInputViewModel { ProductId = product.Id, Price = 6m },
            CancellationToken.None);
        Assert.IsType<ViewResult>(duplicate);
        Assert.False(controller.ModelState.IsValid);

        controller.ModelState.Clear();
        await controller.Edit(
            item.Id,
            new SupplierPriceListInputViewModel { ProductId = product.Id, Price = 7m },
            CancellationToken.None);
        Assert.Equal(7m, (await context.SupplierPriceLists.AsNoTracking().SingleAsync()).Price);

        await controller.Delete(item.Id, CancellationToken.None);
        Assert.Empty(await context.SupplierPriceLists.ToListAsync());
    }
}