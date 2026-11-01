# Customer price list

We need to create a customer price liste, so when we are creating a new sales order and the CFe is selecting the products, the prices of the product can be filled up automaticaly and doesn't depend on the manual input.

# Table structure

Table name: CustomerPriceList

This table needs to have the audit fields.

|column|data type|nullable|comments|
|------|----------|-------|--------|
|id|int|false|autoincrement, PK|
|ProductId|int|false|fk to the products table|
|CustomerId|int|false|fk to the customers table|
|price|double|false||


# Screen to fill up.

Inside the customer catalog, http://localhost:5129/Customers, we need to add a new button called edit customer price list, there we should see an standard catalog a list with the products prices, add, edit, delete etc.

# Implementation notes

- `price` is stored as `decimal(18,4)` (consistent with `SalesOrderDetail.Price`) rather than `double`.
- Unique index on (`CustomerId`, `ProductId`): one price per product per customer.
- Screen: Customers index → "Edit customer price list" (`/CustomerPriceList/Index?customerId=`), with add/edit/delete. Rows are hard-deleted.
- New authorization actions: `VIEW/CREATE/EDIT/DELETE_CUSTOMER_PRICE_LIST` (seeded at startup).
- Sales order edit: choosing a product pre-fills the price from the customer's price list; the field remains editable.
- Migration `CustomerPriceList` must be applied with `dotnet ef database update`.
