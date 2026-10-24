# Sales Orders

We need to create module where the sales analist can load a sales order, by inputing the customer, delivery information, choose the products quantities and prices.

We need a dynamic screen where the CFE can load the sales order header and then input as many products as he or she wants of course only from the producst table.

A function to print the sales order in paper is needed so we can give a copy to the customer and keep one for the bussines and we also need a delivery note to give the driver a copy.

# Data model 

## Order Table

The existing Order table needs to be removed from the model, the tables has never been used.

## Sales Order Header

This table must have it's audit fields.

| Column | Data type | Nullable | Comments |
| --- | --- | --- | --- |
|id|int|false|pk autoincrement|
|OrderNumber|nvarchar(10)|false|uniq|
|CustomerId|int|false|FK to Customer Table|
|DeliveryAddress|varchar(359)|false||
|DeliveryDate|datetime|false||
|StatusId|int|false|fk to Status table|
|TaxId|int|false|fk to Tax table|
|DiscountPercent1|decimal(18,4)|false||
|DiscountPercent2|decimal(18,4)|false||
|DiscountPercent3|decimal(18,4)|false||
|Subtotal|decimal(18,4)|false||
|TaxAmount|decimal(18,4)|false||
|Total|decimal(18,4)|false||



## Sales Order Detail

This table must have it's audit fields, in this case the deletes need to be hard deletes.

| Column | Data type | Nullable | Comments |
| --- | --- | --- | --- |
|id|int|false|pk autoincrement|
|SalesOrderHeaderId|int|false|FK to SalesOrderHeader Table|
|ProductId|int|false|FK to Product Table|
|DiscountPercent|decimal(18,4)|true||
|Qty|decimal(18,4)|false||
|Price|decimal(18,4)|false||
|SubTotal|decimal(18,4)|false||

## Rules

### Sales Order number

The logic of this SalesOrderHeader.ordernumber should be {year}-{consecutive number leaded by 0 to the left until having 5 lenght}.

Once the year changes the consecutive number needs to be reset to one.

example format YYYY-00001

### Status

The status available values in the drop down, should be filtered in the Statuses where the Entity = "SalesOrder".

When the sales order header is created, the status should be Draft.

### Tax

The drop down of the tax needs to be feeded from the tax table, once the value is selected in order to calculate the tax we should take the tax.value to calculate the total tax.


### Calculations Order Details

SubTotal = (price * qty) - discount%


### Calculations Order Header
The calculation needs to follow these steps:

1. calculate Subtotal = sum(all the details subtotal)
2. calculate discount1; Subtotaldiscount1 = Subtotal -discount1%
2.1. calculate discount2; Subtotaldiscount2 = Subtotal -discount2%
2.2. calculate discount3; Subtotaldiscount3 = Subtotal -discount3%
3. calculate tax; TaxAmount = Subtotal * tax.value
4. calculate Total; Total = Subtotal - discount1 - discount2 - discount3 + tax


## Flows

### 1. Create Header.

the user needs to input the customer information, delivery information, tax and discounts in case it applies, the order header should be created with Draft status.
The order number needs to be calculated automatically.

### 2. Create Details.

the user needs to input the producst that are going to be sell to the customer, input the qty of products and the price, and in case the product has discount it can be added.
Once the items are created the approval button must be available and the person with the correct permition should approve the sales order.

### 3. Edit Details

If an edition is done, the order status needs to be reset to draft, once the edition is done the approval button must be available and the person with the correct permition should approve the sales order.

### 4. Print documents

The orders can be printed once thay have the items created, the print we need is in pdf in a paper size of half A4, where we can see the customer information, delivery information and the detailed products with the qty, price sub total, total.

We need to print the PDF with 3 copies.

### 5. Delivery acknoledge

The users needs to have a delivery button to acknoledge the sales order was delivered, and change the status to delivered. Once the order is delivered there is no authorization to a regular user to edit the order. If a edition is needed a special permition needs to be asigned.





