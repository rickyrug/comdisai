# Status

We need a dictionary to have the posible status of any object like the orders or the customers or the suppliers.

This dictionary should be placed on Catalogs --> Configuration --> Status

## Status definition

we need the audit fields

|column|data type|nullable|comments|
|------|---------|--------|---------|
|id|int|false|pk and autoincrement|
|name|nvarchar(255)|false|uniq convined with entity|
|entity|nvarchar(20)|false|uniq convined with name|
