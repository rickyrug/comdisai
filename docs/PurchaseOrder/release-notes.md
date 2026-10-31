# Purchase Orders

## Data and Migration

Purchase Orders add the `PurchaseOrderHeaders` and `PurchaseOrderDetails` tables. Both use the common audit fields. Detail lines are hard-deleted when removed; deleting a header cascades to its lines. The header records the expected supply date separately from the nullable whole-order receipt fields `ReceivedAtUtc` and `ReceivedBy`.

The migration creates only Purchase Order tables and indexes. It does not recreate or drop the legacy `Orders` table, which was removed by the Sales Orders migration.

Apply the migration after deployment approval:

```sh
dotnet ef database update --project ComdisAI/ComdisAI.csproj --startup-project ComdisAI/ComdisAI.csproj
```

## Setup and Workflow

Create active `Draft`, `Approved`, `Supplied`, and `Cancelled` rows in `Statuses` with `Entity = "PurchaseOrder"`. The feature does not silently create dictionary statuses. New orders start Draft; only Draft orders with lines can be approved, and only Approved orders with lines can be acknowledged as Supplied. Cancellation is limited to Draft and Approved.

The authorization seeder registers the Purchase Order actions. Assign them to roles through the role-action screens; registering an action does not grant it automatically. A creator may enter the initial details while the order remains an empty Draft. Subsequent edits require edit permission. Supplied-order edits require the separate elevated action and preserve Supplied status.

The application assigns `YYYY-00001` order numbers using the UTC year and a serializable allocation transaction. Pricing is recalculated server-side with four-place midpoint-away-from-zero rounding. Header discounts apply sequentially; tax is fractional and applies to the post-discount balance.

## PDF Output

Printing requires at least one detail line and does not require approval. PDFs use A5 portrait pages and contain Supplier Copy, Business Copy, and Supply Note copies. Supplier and Business copies include prices and totals; the Supply Note is quantity-only and includes a receiver signature line. Long orders paginate within each copy.

## Verification

`PurchaseOrderTests` covers pricing, numbering, Draft creation/audit values, edit and supply transitions, receipt actor/time, PDF dimensions and pagination, migration shape, and authorization action registration.