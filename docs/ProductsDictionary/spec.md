# Products dictionary

The product dictionary is important to know what is going to be sell or buy.

# Maintenance window

We need the maintance windows that shows the list, with a search bar. Create a product, edit a product and delete.

The delete should be a logical delete.

# Table structure

This table needs to have the audit fields.

|column|data type|nullable|comments|
|------|----------|-------|--------|
|id|int|false|autoincrement, PK|
|Name|nvarchar(255)|false||
|Code|nvarchar(5)|false|uniq|
|Uom|int|false|FK to Uom table|
|ProductCategory|int|false|FK to ProductCategories|


# Logic Code

The product code needs to have a lenght of 5 caracters with leading left 0. But the format needs to be - {ProductCategories.Prefix}{leding 0}{userinput}


