# Status

We need a dictionary to have the posible tax to be charged or payed, and sicen the tax law changes constatly we need to add this dictionary to be updatable.

This dictionary should be placed on Catalogs --> Configuration --> Tax

## Tax definition

we need the audit fields

|column|data type|nullable|comments|
|------|---------|--------|---------|
|id|int|false|pk and autoincrement|
|name|nvarchar(255)|false|uniq |
|value|decimal(18,4)|false||