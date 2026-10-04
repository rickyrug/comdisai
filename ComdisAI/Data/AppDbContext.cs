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
    public DbSet<Bank> Banks => Set<Bank>();

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
        modelBuilder.Entity<Bank>(entity =>
        {
            entity.Property(bank => bank.Name)
                .HasMaxLength(255);
            entity.Property<string>("NormalizedName")
                .HasMaxLength(255)
                .IsRequired();
            entity.HasIndex("NormalizedName")
                .IsUnique()
                .HasDatabaseName("IX_Banks_NormalizedName");
            entity.HasQueryFilter(bank => !bank.IsDeleted);
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
                SetNormalizedDictionaryName(entry.Entity);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
                entry.Property(entity => entity.CreatedBy).IsModified = false;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.UpdatedBy = actor;
                SetNormalizedDictionaryName(entry.Entity);

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
                else if (entry.Entity is Bank { IsDeleted: true, DeletedAtUtc: null } bank)
                {
                    bank.DeletedAtUtc = now;
                    bank.DeletedBy = actor;
                }
            }
            else if (entry.State == EntityState.Deleted && entry.Entity is Customer or Uom or Bank)
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
                    case Bank bank:
                        bank.IsDeleted = true;
                        bank.DeletedAtUtc = now;
                        bank.DeletedBy = actor;
                        break;
                }
                entity.UpdatedAtUtc = now;
                entity.UpdatedBy = actor;
                SetNormalizedDictionaryName(entity);
                entry.State = EntityState.Modified;
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
                entry.Property(entity => entity.CreatedBy).IsModified = false;
            }
        }
    }

    private void SetNormalizedDictionaryName(AuditableEntity entity)
    {
        if (entity is Uom uom)
            Entry(uom).Property<string>("NormalizedName").CurrentValue = uom.Name.Trim().ToUpperInvariant();
        else if (entity is Bank bank)
            Entry(bank).Property<string>("NormalizedName").CurrentValue = bank.Name.Trim().ToUpperInvariant();
    }
}
