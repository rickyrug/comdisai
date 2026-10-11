# Release Notes

## Unreleased — Products Dictionary

### Added
- Product dictionary MVC screens with searchable listing, create, edit, and logical delete.
- Required product name (maximum 255 characters), case-insensitive unique five-character product code (shorter input is left-padded with zeros), and required unit-of-measure relationship.
- Product audit and logical-delete metadata, active-product query filtering, and authorization actions/navigation.
- `ProductsDictionary` EF Core migration (not applied automatically).

## Unreleased — Application Menu

### Changed
- Reorganized the shared top navigation into Catalogs, Sales, and Administrator > Security dropdowns.
- Navigation destinations and their parent menus are shown according to the selected active role's view permissions.
- Kept the menu responsive for mobile use and retained the existing account and role controls.

## Unreleased — Authorization Layer

### Added
- Authenticated-by-default access for application routes, with action-code authorization on each protected controller endpoint.
- Active-role selection and role switching; permissions are evaluated only from the selected, currently assigned, non-deleted role.
- Access-denied page and navigation links filtered to the active role's grants.
- Idempotent bootstrap of the initial administrator, role, and authorization actions using a hashed password.

### Notes
- Apply EF Core migrations before first startup. Configure `BootstrapAdmin:InitialPassword` through a secret provider (for example, the `BootstrapAdmin__InitialPassword` environment variable); optionally set `BootstrapAdmin:Email`. The initial password is only used when creating the admin account and is not reset on subsequent starts.
- The admin bootstrap seeds missing authorization actions and assignments but fails startup if a required admin account, role, or action has been logically deleted; restore it before restarting.
- No migration is required for this layer.

## Unreleased — Actions Dictionary

### Added
- Actions dictionary MVC screen with list, create, edit, and logical delete.
- Required action code (maximum 50 characters) and description (maximum 255 characters); codes are trimmed and unique without regard to case, and remain reserved after logical deletion.
- Action audit metadata, query filtering for deleted actions, and an EF Core migration (not applied automatically).

## Unreleased — Authentication Layer

### Added
- Database-backed user dictionary with create, edit, list, logical delete, and manual password reset.
- Cookie-based sign-in and sign-out, with hashed passwords and a UTC last-login timestamp.
- Case-insensitive unique email addresses, shared audit metadata, and logical-delete query filtering.
- `AuthenticationLayer` EF Core migration (not applied automatically).

### Notes
- Authentication does not yet restrict access to application or user-management routes; authorization is deferred to a later layer.
- Apply migrations with `dotnet ef database update` before using the user dictionary.

## Unreleased — Bank Dictionary

### Added
- Bank dictionary MVC screen with list, create, edit, and logical delete.
- Required Bank name (maximum 255 characters), trimmed on write and unique without regard to case. A deleted Bank's name remains reserved.
- Bank audit metadata and EF Core migration, reusing the shared audit/save pipeline.

## Unreleased — UOM Dictionary

### Added
- UOM dictionary MVC screen with list, create, edit, and logical delete.
- Required UOM name (maximum 255 characters), trimmed on write and unique without regard to case. A deleted UOM's name remains reserved.
- UOM audit metadata and migration, reusing the shared audit/save pipeline.

## Earlier — Customer Dictionary and Audit Fields

### Added
- Customer dictionary MVC screen with list, create, edit, and logical delete.
- Customer fields from `docs/CustomerDictionary/spec.md`: required name, address, and RFC with the specified length limits.
- Shared `CreatedAtUtc`, `CreatedBy`, `UpdatedAtUtc`, and `UpdatedBy` fields on Customer, Product, and Order.
- Automatic audit stamping in `AppDbContext`, using the authenticated `NameIdentifier` claim or `system` when unavailable.
- Customer deletion metadata (`IsDeleted`, `DeletedAtUtc`, `DeletedBy`) and a global query filter that hides logically deleted customers.
- `CustomerDictionaryAndAudit` EF Core migration, including a legacy-data backfill for existing products and orders.

## Earlier — Repository & Unit of Work

### Added
- `IGenericRepository<T>` / `GenericRepository<T>` (`Repositories/`): `GetByIdAsync`, `GetAllAsync`, `AddAsync`, `Update`, `Delete`. Async methods accept a `CancellationToken`.
- `IUnitOfWork` / `UnitOfWork`: lazily created, cached repositories via `Repository<T>()`, `SaveChangesAsync(CancellationToken)` as the single commit point, and `IAsyncDisposable` to dispose the `DbContext`.
- `AppDbContext` (`Data/`) using SQLite, with `Product` and `Order` sample entities (`Models/`).
- `OrdersController`: example that creates an order and decrements product stock in one atomic `SaveChangesAsync` call.
- `DefaultConnection` connection string in `appsettings.json` (`Data Source=comdisai.db`).

### Changed
- `Program.cs` registers `AppDbContext` (SQLite), the open-generic `IGenericRepository<>`, and `IUnitOfWork` as scoped services.

### Notes
- Apply migrations with `dotnet ef database update` before using the database.
- `OrdersController.Index` has no view yet.
