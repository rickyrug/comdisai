using ComdisAI.Data;
using ComdisAI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
using Xunit;

namespace ComdisAI.Tests;

public sealed class CustomerDefaultTaxTests
{
    [Fact]
    public async Task Customer_persists_optional_default_tax()
    {
        Batteries.Init();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());
        await context.Database.EnsureCreatedAsync();

        var tax = new Tax { Name = "VAT", Value = 0.16m };
        context.Taxes.Add(tax);
        await context.SaveChangesAsync();
        context.Customers.AddRange(
            new Customer { Name = "A", Address = "x", RFC = "R1", DefaultTaxId = tax.Id },
            new Customer { Name = "B", Address = "y", RFC = "R2" });
        await context.SaveChangesAsync();

        var customers = await context.Customers.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(tax.Id, customers[0].DefaultTaxId);
        Assert.Null(customers[1].DefaultTaxId);
    }
}
