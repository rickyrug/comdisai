# Purchase Orders

Buyers create a purchase order for a supplier, enter the expected supply address and date, and add any number of product lines. The module provides approval, whole-order supply acknowledgment, and a three-copy PDF.

The legacy `Orders` table was removed by the Sales Orders migration. The Purchase Orders migration must not recreate or drop it.

## Data Model

### Purchase Order Header

All fields are required unless marked nullable. The header uses the common audit fields `CreatedAtUtc`, `CreatedBy`, `UpdatedAtUtc`, and `UpdatedBy`.

| Column | Type | Nullable | Notes |
| --- | --- | --- | --- |
| Id | int | No | Primary key, auto-increment |
| OrderNumber | nvarchar(10) | No | Unique; `YYYY-00001` format |
| SupplierId | int | No | FK to Supplier |
| SupplyAddress | varchar(359) | No | Initial value comes from the selected supplier; may be changed for the order |
| SupplyDate | datetime | No | Expected supply date |
| StatusId | int | No | FK to Status |
| TaxId | int | No | FK to Tax |
| DiscountPercent1 | decimal(18,4) | No | Percentage from 0 to 100 |
| DiscountPercent2 | decimal(18,4) | No | Percentage from 0 to 100 |
| DiscountPercent3 | decimal(18,4) | No | Percentage from 0 to 100 |
| Subtotal | decimal(18,4) | No | Sum of calculated detail subtotals |
| TaxAmount | decimal(18,4) | No | Calculated tax |
| Total | decimal(18,4) | No | Calculated total |
| ReceivedAtUtc | datetime | Yes | UTC timestamp set when the order is acknowledged as Supplied |
| ReceivedBy | nvarchar(255) | Yes | User identifier that acknowledged supply |

### Purchase Order Detail

Detail lines use the same common audit fields. Removing a line is a hard delete; deleting a header cascades to its lines.

| Column | Type | Nullable | Notes |
| --- | --- | --- | --- |
| Id | int | No | Primary key, auto-increment |
| PurchaseOrderHeaderId | int | No | FK to PurchaseOrderHeader |
| ProductId | int | No | FK to Product |
| DiscountPercent | decimal(18,4) | Yes | Percentage from 0 to 100; null means zero |
| Qty | decimal(18,4) | No | Must be greater than zero |
| Price | decimal(18,4) | No | Must be zero or greater |
| SubTotal | decimal(18,4) | No | Server-calculated line subtotal |

## Rules

### Order Number

Use the current UTC year and the next five-digit sequence for that year, for example `2026-00001`. A new year starts at `00001`. Number selection and insertion must be concurrency-safe; the unique index is the final duplicate guard. Reject malformed existing sequence values and report exhaustion after `99999` rather than issuing an invalid number.

### Status

Configure active `Draft`, `Approved`, `Supplied`, and `Cancelled` statuses with `Entity = "PurchaseOrder"`. The status filter lists only statuses for that entity. Status changes happen through workflow actions, not free-form editing of the header.

- New headers start in Draft.
- Only a Draft order with at least one detail line can be approved.
- Only an Approved order with at least one line can be acknowledged as Supplied.
- Saving edits to a non-Supplied order returns it to Draft, including an Approved order.
- A Supplied order cannot be edited with regular edit permission. A user with the special supplied-order edit permission may edit it, and it remains Supplied.
- Cancellation is allowed only from Draft or Approved and requires its own permission. Supplied orders cannot be cancelled.

### Catalog Selection

New orders can select active suppliers, products, and taxes only. Existing orders continue to display their referenced records if those records are later deactivated. The selected supplier address is offered as the initial supply address. The buyer selects a tax explicitly.

### Calculations

All percentage inputs are rates from 0 through 100. `Tax.Value` is a fractional rate (`0.16` means 16%) and must be between 0 and 1. Round calculated monetary values to four decimal places after each step using midpoint-away-from-zero rounding; reject values outside `decimal(18,4)`.

1. Detail subtotal: `Price × Qty × (1 − DiscountPercent / 100)`. A null detail discount is zero.
2. Header subtotal: sum of the rounded detail subtotals.
3. Apply header discounts sequentially to the running balance: Discount 1, then Discount 2, then Discount 3.
4. Tax amount: post-discount balance multiplied by `Tax.Value`.
5. Total: post-discount balance plus tax amount.

The server recalculates detail and header amounts on every save. Posted subtotal, tax amount, and total values are never authoritative.

## Workflow

1. **Create:** Select supplier, expected supply date/address, tax, and optional header discounts. The order number is assigned automatically and the header starts in Draft.
2. **Add details:** Select products, quantity, unit price, and optional line discount. Multiple lines can be added or removed dynamically. Once at least one line exists, a user with approval permission can approve the order.
3. **Edit:** Changes to a non-Supplied order reset its status to Draft and require approval again. Removed lines are hard-deleted. Supplied-order edits require the special permission and preserve the Supplied status.
4. **Print:** Orders with at least one detail line can be printed without requiring approval. Produce three labeled A5 portrait (half-A4) copies: Supplier Copy, Business Copy, and Supply Note. Supplier and Business copies include supplier identity, supply address/date, product, quantity, UOM, price, discounts, subtotal, tax, and total. The Supply Note includes supplier/supply information, product, quantity, UOM, and a receiver signature line without pricing. Long orders continue across pages within each copy.
5. **Acknowledge supply:** A user with supply permission can acknowledge the whole order only after approval. Set status to Supplied and record the UTC acknowledgment timestamp and user identifier. Partial receipts are not supported.
6. **Cancel:** A user with cancellation permission can cancel a Draft or Approved order.

## Permissions

Register these action codes for role assignment: `VIEW_PURCHASE_ORDER`, `CREATE_PURCHASE_ORDER`, `EDIT_PURCHASE_ORDER`, `APPROVE_PURCHASE_ORDER`, `PRINT_PURCHASE_ORDER`, `SUPPLY_PURCHASE_ORDER`, `EDIT_SUPPLIED_PURCHASE_ORDER`, and `CANCEL_PURCHASE_ORDER`. Creating a header requires create permission; the creator can enter initial details while the Draft is empty. Later edits require edit permission.





