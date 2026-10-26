using ComdisAI.Data;
using ComdisAI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class SupplierTests
{
    [Fact]
    public async Task Supplier_is_soft_deleted_and_filtered()
    {
        Batteries.Init();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());
        await context.Database.EnsureCreatedAsync();

        var bank = new Bank { Name = "B1" };
        context.Banks.Add(bank);
        await context.SaveChangesAsync();
        var supplier = new Supplier { Name = "S", Address = "a", RFC = "R", BankId = bank.Id, BankAccount = "123" };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        context.Suppliers.Remove(supplier);
        await context.SaveChangesAsync();

        Assert.Empty(await context.Suppliers.ToListAsync());
        var stored = await context.Suppliers.IgnoreQueryFilters().SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAtUtc);
    }
}
