# Application Menu

The application uses a shared top navigation that remains available throughout the application. Its menu groups use dropdowns and follow this hierarchy:

- Home
- Catalogs
  - Products
  - Customers
  - Suppliers
  - Configuration
    - Banks
    - Units of Measure
    - Product Categories
    - Status
    - Tax
  - Administrator
    - Security
      - Actions
      - Roles
      - Users
    - Application Logs
- Sales
  - Sales Orders
  - Sales Reports
  - Customer Payments
- Procurement
  - Purchase Orders
  - Purchase Reports
  - Supplier Payments

Navigation links are shown only when the selected active role grants the corresponding view action. A dropdown group is hidden when none of its destinations are available to that role. Catalog destinations retain their existing hierarchy and authorization behavior. The existing sign-in, active-role, role-switch, and sign-out controls remain available; Privacy remains in the footer.