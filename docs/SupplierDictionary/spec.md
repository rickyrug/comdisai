# Supplier dictionary

We need a Supplier dictionary, where we can store our Supplier information. We need a screen where we can see the Supplier in a list, add new Supplier, edit Supplier, delete Supplier, the deletion should be logical not fisical.

## Table definition

|column|data type|nullable|comments|
|------|---------|--------|---------|
|id|int|false|pk and autoincrement|
|name|nvarchar(255)|false||
|Address|nvarchar(255)|false||
|RFC|nvarchar(25)|false||
|BankId|int|false||
|BankAccount|nvarchar(25)|false||