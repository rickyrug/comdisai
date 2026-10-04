# Release Notes

## Unreleased — Customer Dictionary and Audit Fields

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
