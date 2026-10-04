using ComdisAI.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ComdisAI.Data;

public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IHttpContextAccessor httpContextAccessor) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Customer>().HasQueryFilter(customer => !customer.IsDeleted);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditFields()
    {
        var now = DateTime.UtcNow;
        var actor = httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.CreatedBy = actor;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.UpdatedBy = actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
                entry.Property(entity => entity.CreatedBy).IsModified = false;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.UpdatedBy = actor;

                if (entry.Entity is Customer { IsDeleted: true, DeletedAtUtc: null } customer)
                {
                    customer.DeletedAtUtc = now;
                    customer.DeletedBy = actor;
                }
            }
            else if (entry.State == EntityState.Deleted && entry.Entity is Customer customer)
            {
                customer.IsDeleted = true;
                customer.DeletedAtUtc = now;
                customer.DeletedBy = actor;
                customer.UpdatedAtUtc = now;
                customer.UpdatedBy = actor;
                entry.State = EntityState.Modified;
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
                entry.Property(entity => entity.CreatedBy).IsModified = false;
            }
        }
    }
}
