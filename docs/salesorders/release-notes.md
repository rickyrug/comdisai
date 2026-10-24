# Sales Orders

## Summary

Sales Orders replace the unused legacy `Order` entity with a header-and-detail workflow. A user creates a Draft order for an active customer, enters delivery details, tax, header discounts, and product lines, then requests approval. Approved orders can be marked Delivered. Sales order PDFs contain separate customer, business, and driver copies.

The source specification is [Sales Order spec](../SalesOrder/spec.md).

## Data model and migration

The `SalesOrderHeaders` table stores the order number, customer, delivery address/date, status, tax, three header discount rates, subtotal, tax amount, total, and the common audit fields (`CreatedAtUtc`, `CreatedBy`, `UpdatedAtUtc`, `UpdatedBy`). `SalesOrderDetails` stores the parent, product, optional line discount rate, quantity, unit price, calculated line subtotal, and the same audit fields.

All monetary, quantity, and percentage values use `decimal(18,4)` precision. Customer, status, tax, and product references are required and restricted from physical deletion while referenced; deleting a header cascades to its detail lines. Detail lines are intentionally hard-deleted when removed from an order. The existing audit hook supplies UTC timestamps and the authenticated actor ID, falling back to `system`.

The `SalesOrders` EF migration drops the unused `Orders` table and creates both new tables, required foreign keys, and indexes. The old table is not copied into the new model. Review the migration before applying it; it has not been applied to the configured database as part of this change.

To apply after deployment approval:

```sh
dotnet ef database update --project ComdisAI/ComdisAI.csproj --startup-project ComdisAI/ComdisAI.csproj
```

## Number assignment

The application assigns order numbers as `YYYY-00001` using the current UTC year. The first order for each UTC year starts at `00001`; each following order increments the largest existing sequence for that year. The unique index prevents duplicates, and number selection/insertion execute in a serializable database transaction. The five-digit sequence supports a maximum of `99999` orders per year; the application reports exhaustion instead of issuing a malformed number.

## Calculations

All percentage inputs are rates from 0 through 100. Values are rounded to four decimal places after each calculation using midpoint-away-from-zero rounding. Calculated values that cannot fit the persisted `decimal(18,4)` range are rejected instead of being stored:

1. A line subtotal is `price × quantity × (1 − line discount% / 100)`. A missing line discount is treated as zero.
2. Header `Subtotal` is the sum of rounded line subtotals.
3. Header discounts are applied in order to the running balance: discount 1, then discount 2 on the result, then discount 3 on that result.
4. `Tax.Value` is a fractional rate: `0.16` means 16%. Tax is calculated on the amount remaining after all three header discounts. Values below 0 or above 1 are rejected for sales orders.
5. `Total` is the post-discount taxable amount plus tax.

The server recalculates detail and header totals on every save; posted subtotal/total values are not accepted as authoritative. For example, a line of 2 units at 100 with a 10% line discount is 180. With a 10% first header discount, the next balance is 162; later discounts apply to that running balance.

## Status and edit rules

The active `Statuses` dictionary must contain `Draft`, `Approved`, and `Delivered` rows with `Entity = "SalesOrder"`. Statuses are not silently created by the feature. If any required row is missing, the create/approval/delivery flow reports the missing setup.

- New headers begin in Draft. Header creation and detail entry are separate steps.
- Only a Draft order with at least one detail line can be approved.
- Saving an edit to any non-delivered order resets its status to Draft, including edits to a previously approved order.
- A Draft or Approved order can be marked Delivered only through the delivery action.
- A Delivered order can only be edited by a user with `EDIT_DELIVERED_SALES_ORDER`. These privileged edits leave it Delivered; other edit attempts are denied.
- Orders can be printed once they contain at least one detail line; approval is not required for printing.

The application displays historic orders even when a referenced customer, product, status, or tax is later soft-deleted. New selections use active catalog records only.

## Permissions

The authorization seeder registers these action codes for assignment to roles:

| Action code | Purpose |
| --- | --- |
| `VIEW_SALES_ORDER` | List and view orders |
| `CREATE_SALES_ORDER` | Create order headers |
| `EDIT_SALES_ORDER` | Edit non-delivered orders |
| `APPROVE_SALES_ORDER` | Approve Draft orders |
| `PRINT_SALES_ORDER` | Download PDFs |
| `DELIVER_SALES_ORDER` | Mark Approved orders Delivered |
| `EDIT_DELIVERED_SALES_ORDER` | Edit Delivered orders |

Adding an action to the catalog does not grant it to non-admin roles. Assign the actions through the existing role-action management screens. Sales Orders appears in the Sales menu for users granted `VIEW_SALES_ORDER`.

## PDF output

The print action generates an A5 (portrait half-A4) PDF with three labeled copies: **Customer Copy**, **Business Copy**, and **Delivery Note**. Each copy includes the order number/status, customer, delivery address/date, product, quantity, unit price, line subtotal, discounts, tax, and order total. Long orders continue on additional A5 pages for each copy.

PDF generation uses PDFsharp 6.2.4 and an embedded Roboto font so rendering does not depend on system-installed fonts. Roboto is distributed under the SIL Open Font License; its license is at [OFL.txt](../../ComdisAI/Resources/Fonts/OFL.txt).

## Verification

Focused tests cover price/discount/tax arithmetic, four-place rounding, yearly numbering and exhaustion, Draft creation/audit values, approval and delivery transitions, edit permission/status behavior, hard deletion of removed detail lines, PDF page count/size, authorization action registration, and migration table/index shape.
