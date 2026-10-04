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
    public DbSet<Uom> Uoms => Set<Uom>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Customer>().HasQueryFilter(customer => !customer.IsDeleted);
        modelBuilder.Entity<Uom>(entity =>
        {
            entity.Property(uom => uom.Name)
                .HasMaxLength(255);
            entity.Property<string>("NormalizedName")
                .HasMaxLength(255)
                .IsRequired();
            entity.HasIndex("NormalizedName")
                .IsUnique()
                .HasDatabaseName("IX_Uoms_NormalizedName");
            entity.HasQueryFilter(uom => !uom.IsDeleted);
        });
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
                SetNormalizedUomName(entry.Entity);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
                entry.Property(entity => entity.CreatedBy).IsModified = false;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.UpdatedBy = actor;
                SetNormalizedUomName(entry.Entity);

                if (entry.Entity is Customer { IsDeleted: true, DeletedAtUtc: null } customer)
                {
                    customer.DeletedAtUtc = now;
                    customer.DeletedBy = actor;
                }
                else if (entry.Entity is Uom { IsDeleted: true, DeletedAtUtc: null } uom)
                {
                    uom.DeletedAtUtc = now;
                    uom.DeletedBy = actor;
                }
            }
            else if (entry.State == EntityState.Deleted && entry.Entity is Customer or Uom)
            {
                var entity = entry.Entity;
                switch (entity)
                {
                    case Customer customer:
                        customer.IsDeleted = true;
                        customer.DeletedAtUtc = now;
                        customer.DeletedBy = actor;
                        break;
                    case Uom uom:
                        uom.IsDeleted = true;
                        uom.DeletedAtUtc = now;
                        uom.DeletedBy = actor;
                        break;
                }
                entity.UpdatedAtUtc = now;
                entity.UpdatedBy = actor;
                SetNormalizedUomName(entity);
                entry.State = EntityState.Modified;
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
                entry.Property(entity => entity.CreatedBy).IsModified = false;
            }
        }
    }

    private void SetNormalizedUomName(AuditableEntity entity)
    {
        if (entity is Uom uom)
            Entry(uom).Property<string>("NormalizedName").CurrentValue = uom.Name.Trim().ToUpperInvariant();
    }
}
