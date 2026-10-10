# Actions dictionary

We need a actions dictionary, where we can have a actions dictionary that we will need to use in the authorization layer. We need a screen where we can see the a list, add new , edit , delete , the deletion should be logical not fisical.

## Table definition

|column|data type|nullable|comments|
|------|---------|--------|---------|
|id|int|false|pk and autoincrement|
|code|nvarchar(50)|false|uniq|
|description|nvarchar(255)|false||
