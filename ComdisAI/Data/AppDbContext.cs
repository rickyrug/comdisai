using ComdisAI.Models;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;
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
    public DbSet<Actions> Actions => Set<Actions>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RoleAction> RoleActions => Set<RoleAction>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

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
        modelBuilder.Entity<Actions>(entity =>
        {
            entity.ToTable("Action");
            entity.HasKey(action => action.Id)
                .HasName("PK_Actions");
            entity.Property(action => action.Code)
                .HasMaxLength(50);
            entity.Property(action => action.Description)
                .HasMaxLength(255);
            entity.Property<string>("NormalizedCode")
                .HasMaxLength(50)
                .IsRequired();
            entity.HasIndex("NormalizedCode")
                .IsUnique()
                .HasDatabaseName("IX_Action_NormalizedCode");
            entity.HasQueryFilter(action => !action.IsDeleted);
        });
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Role");
            entity.Property(role => role.Code)
                .HasMaxLength(50);
            entity.Property(role => role.Description)
                .HasMaxLength(255);
            entity.Property<string>("NormalizedCode")
                .HasMaxLength(50)
                .IsRequired();
            entity.HasIndex("NormalizedCode")
                .IsUnique()
                .HasDatabaseName("IX_Role_NormalizedCode");
            entity.HasQueryFilter(role => !role.IsDeleted);
        });
        modelBuilder.Entity<RoleAction>(entity =>
        {
            entity.ToTable("RoleActions");
            entity.Property(roleAction => roleAction.RoleId)
                .HasColumnName("idRole");
            entity.Property(roleAction => roleAction.ActionId)
                .HasColumnName("idAction");
            entity.HasIndex(roleAction => new { roleAction.RoleId, roleAction.ActionId })
                .IsUnique()
                .HasDatabaseName("IX_RoleActions_idRole_idAction");
            entity.HasQueryFilter(roleAction => !roleAction.Role.IsDeleted && !roleAction.Action.IsDeleted);
            entity.HasOne(roleAction => roleAction.Role)
                .WithMany(role => role.RoleActions)
                .HasForeignKey(roleAction => roleAction.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(roleAction => roleAction.Action)
                .WithMany(action => action.RoleActions)
                .HasForeignKey(roleAction => roleAction.ActionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.Property(userRole => userRole.RoleId)
                .HasColumnName("idRole");
            entity.Property(userRole => userRole.UserId)
                .HasColumnName("idUser");
            entity.HasIndex(userRole => new { userRole.UserId, userRole.RoleId })
                .IsUnique()
                .HasDatabaseName("IX_UserRoles_idUser_idRole");
            entity.HasQueryFilter(userRole => !userRole.User.IsDeleted && !userRole.Role.IsDeleted);
            entity.HasOne(userRole => userRole.User)
                .WithMany(user => user.UserRoles)
                .HasForeignKey(userRole => userRole.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(userRole => userRole.Role)
                .WithMany(role => role.UserRoles)
                .HasForeignKey(userRole => userRole.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(user => user.Name)
                .HasMaxLength(255);
            entity.Property(user => user.Surname)
                .HasMaxLength(255);
            entity.Property(user => user.Email)
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(user => user.PasswordHash)
                .HasMaxLength(255)
                .IsRequired();
            entity.Property<string>("NormalizedEmail")
                .HasMaxLength(255)
                .IsRequired();
            entity.HasIndex("NormalizedEmail")
                .IsUnique()
                .HasDatabaseName("IX_Users_NormalizedEmail");
            entity.HasQueryFilter(user => !user.IsDeleted);
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
                SetNormalizedEntityFields(entry.Entity);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
                entry.Property(entity => entity.CreatedBy).IsModified = false;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.UpdatedBy = actor;
                SetNormalizedEntityFields(entry.Entity);

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
                else if (entry.Entity is Actions { IsDeleted: true, DeletedAtUtc: null } action)
                {
                    action.DeletedAtUtc = now;
                    action.DeletedBy = actor;
                }
                else if (entry.Entity is Role { IsDeleted: true, DeletedAtUtc: null } role)
                {
                    role.DeletedAtUtc = now;
                    role.DeletedBy = actor;
                }
                else if (entry.Entity is User { IsDeleted: true, DeletedAtUtc: null } deletedUser)
                {
                    deletedUser.DeletedAtUtc = now;
                    deletedUser.DeletedBy = actor;
                }
            }
            else if (entry.State == EntityState.Deleted &&
                     entry.Entity is Customer or Uom or Bank or ComdisAI.Models.Actions or Role or User)
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
                    case Actions action:
                        action.IsDeleted = true;
                        action.DeletedAtUtc = now;
                        action.DeletedBy = actor;
                        break;
                    case Role role:
                        role.IsDeleted = true;
                        role.DeletedAtUtc = now;
                        role.DeletedBy = actor;
                        break;
                    case User deletedUser:
                        deletedUser.IsDeleted = true;
                        deletedUser.DeletedAtUtc = now;
                        deletedUser.DeletedBy = actor;
                        break;
                }
                entity.UpdatedAtUtc = now;
                entity.UpdatedBy = actor;
                SetNormalizedEntityFields(entity);
                entry.State = EntityState.Modified;
                entry.Property(entity => entity.CreatedAtUtc).IsModified = false;
                entry.Property(entity => entity.CreatedBy).IsModified = false;
            }
        }
    }

    private void SetNormalizedEntityFields(AuditableEntity entity)
    {
        if (entity is Uom uom)
            Entry(uom).Property<string>("NormalizedName").CurrentValue = uom.Name.Trim().ToUpperInvariant();
        else if (entity is Bank bank)
            Entry(bank).Property<string>("NormalizedName").CurrentValue = bank.Name.Trim().ToUpperInvariant();
        else if (entity is Actions action)
            Entry(action).Property<string>("NormalizedCode").CurrentValue = action.Code.Trim().ToUpperInvariant();
        else if (entity is Role role)
            Entry(role).Property<string>("NormalizedCode").CurrentValue = role.Code.Trim().ToUpperInvariant();
        else if (entity is User user)
            Entry(user).Property<string>("NormalizedEmail").CurrentValue = User.NormalizeEmail(user.Email);
    }
}
