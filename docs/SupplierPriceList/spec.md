# Supplier price list

We need to create a suplier price liste, so when we are creating a new purchase order and the buyer is selecting the products, the prices of the product can be filled up automaticaly and doesn't depend on the manual input.

# Table structure

Table name: SupplierPriceList

This table needs to have the audit fields.

|column|data type|nullable|comments|
|------|----------|-------|--------|
|id|int|false|autoincrement, PK|
|ProductId|int|false|fk to the products table|
|SupplierId|int|false|fk to the suppliers table|
|price|decimal(18,4)|false||


# Screen to fill up.

Inside the supplier catalog, http://localhost:5129/Suppliers, we need to add a new button called edit supplier price list, there we should see an standard catalog a list with the products prices, add, edit, delete etc.

# Purchase order behavior

- Selecting a product fills its price from the selected supplier's price list when an entry exists.
- Price remains editable on the purchase order and is saved as an order-line snapshot.
- Changing the supplier refreshes line prices that have a matching entry; a missing entry leaves the current line price unchanged.
- Existing line prices are not changed on initial page load or save.


