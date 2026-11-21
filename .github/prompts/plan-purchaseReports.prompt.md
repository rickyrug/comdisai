# Plan: Purchase Reports

Add a Purchase Reports MVC feature that mirrors Sales Reports, using purchase-order expected supply dates, purchase-order statuses, supplier/order rows, period totals, and status summaries. Expose it under Procurement behind a dedicated view action, with focused automated coverage and menu documentation updates.

## Steps

### Phase 1: Branch and authorization
1. Create and switch to a new branch named `feature/purchase-reports` from the current branch. Preserve any unrelated working-tree changes and stop for review if there are conflicts or uncommitted files in the target paths.
2. Add `ViewPurchaseReport = "VIEW_PURCHASE_REPORT"` next to the purchase-order action constants in `AuthorizationActionCodes`.
3. Register `(AuthorizationActionCodes.ViewPurchaseReport, "View purchase reports")` in `AuthorizationSeeder.SeedActions`, allowing startup seeding to create the action and assign it to the admin role. No EF Core migration is required.

### Phase 2: Report data contract and controller
4. Create `ViewModels/PurchaseReports/PurchaseReportViewModel.cs`, mirroring the sales report contract with purchase terminology: `StartDate`, `EndDate`, `StatusIds`, `StatusOptions`, `Orders`, `StatusSummary`, and `PeriodTotal`. Define row and summary types containing expected supply date, supplier name, order number, status name, order count, and totals as applicable.
5. Create `PurchaseReportsController` with `Index` protected by `[RequireAction(AuthorizationActionCodes.ViewPurchaseReport)]`.
6. Implement report behavior to match `SalesReportsController.Index`: default both dates to today only when neither is supplied; require both otherwise; reject end dates before start dates; load only statuses whose normalized `Entity` is `PURCHASEORDER`; reject unknown or cross-entity status IDs; query `PurchaseOrderHeaders` with `IgnoreQueryFilters()` and `AsNoTracking()`; filter inclusively from `SupplyDate >= startDate midnight` through `SupplyDate < day after endDate`; optionally filter selected status IDs; order by `SupplyDate` then `OrderNumber`.
7. Project matching rows into the purchase report view model and calculate `PeriodTotal` plus status summaries in the same manner as the sales report. Use the persisted order `Total`, not a new pricing calculation.

### Phase 3: UI and Procurement navigation
8. Create `Views/PurchaseReports/Index.cshtml` by mirroring the Sales Reports interaction and structure: From/To date inputs, searchable multi-select status dropdown, Run report/Clear actions, validation summary, detail table, empty state, period total, and summary-by-status table.
9. Adapt labels and columns to purchase terminology: title `Purchase Report`, expected supply date, supplier, purchase order, status, and total; use the same `en-US` currency formatting and client-side status filter behavior as the sales report.
10. In `_Layout.cshtml`, add `canViewPurchaseReports` from the new action code, include it in the Procurement menu visibility condition, and add a `Purchase Reports` link targeting `PurchaseReports.Index`. Place it between Purchase Orders and Supplier Payments.

### Phase 4: Tests and documentation
11. Add focused purchase report controller tests. Reuse the in-memory SQLite setup pattern from `PurchaseOrderTests`, creating multiple orders with distinct supply dates, suppliers, totals, and PurchaseOrder/SalesOrder statuses. Verify default dates, inclusive range boundaries, multi-status filtering, ascending date/order sorting, totals, status summaries, invalid ranges, missing dates, and invalid status IDs. Also verify a sales-order status is never offered or accepted.
12. Extend `Authorization_seed_registers_purchase_order_actions` in `PurchaseOrderTests` to assert `ViewPurchaseReport` is seeded, or add the equivalent focused seeder assertion if the test is reorganized.
13. Add an authorization attribute assertion or equivalent request-level test that ensures `PurchaseReportsController.Index` requires only `VIEW_PURCHASE_REPORT`; keep the report endpoint separate from the permission to view individual purchase orders.
14. Update `docs/ApplicationMenu/spec.md` so Procurement lists Purchase Reports between Purchase Orders and Supplier Payments. Add an Unreleased release-notes entry describing the report filters, summaries, dedicated permission, and that no schema migration is required.

## Relevant files

- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Controllers/SalesReportsController.cs` - reference `SalesReportsController.Index` for date defaults, inclusive date range, status normalization, projection, total, and summary logic.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Views/SalesReports/Index.cshtml` - reference the complete report UI and searchable multi-status client script.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/ViewModels/SalesReports/SalesReportViewModel.cs` - reference the report model shape.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Models/PurchaseOrderHeader.cs` - source fields `SupplyDate`, `Supplier`, `OrderNumber`, `Status`, and `Total`.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Controllers/PurchaseReportsController.cs` - create the report query endpoint.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/ViewModels/PurchaseReports/PurchaseReportViewModel.cs` - create the report model and row/summary models.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Views/PurchaseReports/Index.cshtml` - create the purchase report page.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Models/AuthorizationActionCodes.cs` - add the purchase-report permission code.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Data/AuthorizationSeeder.cs` - seed the new action for startup and admin assignment.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Views/Shared/_Layout.cshtml` - add permission-aware Procurement navigation.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI.Tests/PurchaseOrderTests.cs` - extend authorization seeding coverage and reuse established purchase test setup patterns.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI.Tests/PurchaseReportTests.cs` - add report behavior and authorization tests.
- `/Users/rickyrug/Projects/ComdisAI/docs/ApplicationMenu/spec.md` - document the new Procurement destination.
- `/Users/rickyrug/Projects/ComdisAI/docs/release-notes.md` - document the unreleased feature.

## Verification

1. Run the focused tests with `dotnet test ComdisAI.Tests/ComdisAI.Tests.csproj --filter FullyQualifiedName~PurchaseReport` (or the project’s equivalent .NET test syntax for the installed SDK).
2. Run the purchase-order authorization/seeding tests with a filter covering `Authorization_seed_registers_purchase_order_actions` to confirm idempotent seeding includes the new action.
3. Run the full test project with `dotnet test ComdisAI.Tests/ComdisAI.Tests.csproj` and resolve only regressions attributable to this feature.
4. Run `dotnet build ComdisAI/ComdisAI.csproj` and confirm there are no compile errors in the new controller, models, view, or authorization references.
5. Manually start the application and verify: Procurement shows Purchase Reports only for a role with `VIEW_PURCHASE_REPORT`; direct access is denied otherwise; the default report is today; date boundaries include the full selected end day; search/status selection persists after submission; invalid range/status inputs show errors; totals and summaries match the visible rows; Clear resets to today; the menu destination opens correctly at `/PurchaseReports`.

## Decisions

- Branch name: `feature/purchase-reports`.
- “Mirrors” means the report uses the same UX, validation, authorization shape, inclusive date-range handling, persisted totals, and status summary behavior as Sales Reports.
- The reporting date is `SupplyDate` (expected supply date), the direct procurement analogue of the sales report’s `DeliveryDate`, rather than `ReceivedAtUtc` or audit creation time.
- Purchase Reports has its own `VIEW_PURCHASE_REPORT` permission and does not reuse or require `VIEW_PURCHASE_ORDER`; Procurement menu visibility follows the report permission.
- No EF Core migration, shared report abstraction, or changes to purchase-order workflows are included.
