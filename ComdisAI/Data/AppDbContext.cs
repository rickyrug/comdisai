using ComdisAI.Models;
using Microsoft.EntityFrameworkCore;

namespace ComdisAI.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
}
