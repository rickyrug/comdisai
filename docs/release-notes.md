# Release Notes

## Unreleased — Repository & Unit of Work

### Added
- `IGenericRepository<T>` / `GenericRepository<T>` (`Repositories/`): `GetByIdAsync`, `GetAllAsync`, `AddAsync`, `Update`, `Delete`. Async methods accept a `CancellationToken`.
- `IUnitOfWork` / `UnitOfWork`: lazily created, cached repositories via `Repository<T>()`, `SaveChangesAsync(CancellationToken)` as the single commit point, and `IAsyncDisposable` to dispose the `DbContext`.
- `AppDbContext` (`Data/`) using SQLite, with `Product` and `Order` sample entities (`Models/`).
- `OrdersController`: example that creates an order and decrements product stock in one atomic `SaveChangesAsync` call.
- `DefaultConnection` connection string in `appsettings.json` (`Data Source=comdisai.db`).

### Changed
- `Program.cs` registers `AppDbContext` (SQLite), the open-generic `IGenericRepository<>`, and `IUnitOfWork` as scoped services.

### Notes
- No EF Core migrations exist yet; run `dotnet ef migrations add Initial` and `dotnet ef database update` before using the database.
- `OrdersController.Index` has no view yet.
