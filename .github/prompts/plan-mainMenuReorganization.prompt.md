## Plan: Reorganize Main Menu

Reorganize the existing transaction links into two authorization-aware dropdowns, preserving Catalogs exactly: Sales contains Sales Orders, Sales Reports, and Customer Payments; Procurement contains Purchase Orders and Supplier Payments. Use corrected display labels (Procurement and Supplier Payments) while retaining all existing controller routes and permission action codes.

**Steps**
1. **Create the working branch** from the current main branch: `git switch main`, ensure the intended worktree state is understood, then run `git switch -c feature/reorganize-main-menu`. Do not include unrelated uncommitted changes.
2. **Refactor the shared navigation** in `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Views/Shared/_Layout.cshtml`.
   - Leave the complete Catalogs condition and markup unchanged.
   - Replace the three standalone sales links with a top-level `Sales` dropdown shown when at least one of `canViewSalesOrders`, `canViewSalesReports`, or `canViewCustomerPayments` is true.
   - Within Sales, preserve each existing controller/action destination and show each item only when its corresponding permission is granted, in this order: Sales Orders, Sales Reports, Customer Payments.
   - Replace the two standalone purchase/supplier-payment links with a top-level `Procurement` dropdown shown when either `canViewPurchaseOrders` or `canViewSupplierPayments` is true.
   - Within Procurement, preserve each existing controller/action destination and show each item only with its corresponding permission, in this order: Purchase Orders, Supplier Payments.
   - Reuse the existing Bootstrap `nav-item dropdown`, dropdown-toggle button, and `dropdown-menu` conventions. Do not add menu-specific CSS unless responsive validation exposes a problem.
3. **Update the menu contract** in `/Users/rickyrug/Projects/ComdisAI/docs/ApplicationMenu/spec.md`.
   - Document the actual Catalogs hierarchy without changing it.
   - Document Sales and Procurement as top-level groups with the children above.
   - Retain the rule that both child links and their parent dropdown are hidden unless the active role has the required view action.
4. **Add release-note coverage** in `/Users/rickyrug/Projects/ComdisAI/docs/release-notes.md` under the existing Application Menu section, describing the transaction-link regrouping and preservation of Catalogs, routes, account controls, and permission behavior.

**Relevant files**
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Views/Shared/_Layout.cshtml` — replace only the five standalone transaction links after the Catalogs dropdown; reuse the existing `canViewSalesOrders`, `canViewSalesReports`, `canViewCustomerPayments`, `canViewPurchaseOrders`, and `canViewSupplierPayments` gates.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/wwwroot/css/site.css` — reference only; existing `.dropdown-submenu` and mobile behavior are not needed for these one-level Sales/Procurement menus unless validation finds a responsive defect.
- `/Users/rickyrug/Projects/ComdisAI/docs/ApplicationMenu/spec.md` — update the documented hierarchy and visibility rules.
- `/Users/rickyrug/Projects/ComdisAI/docs/release-notes.md` — record the user-visible navigation change.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Controllers/{SalesOrders,SalesReports,CustomerPayments,PurchaseOrders,SupplierPayments}Controller.cs` — reference only; existing `[RequireAction]` protections and Index routes are unchanged.

**Verification**
1. Run `dotnet build /Users/rickyrug/Projects/ComdisAI/ComdisAI/ComdisAI.csproj` to catch Razor compilation issues.
2. Run `dotnet test /Users/rickyrug/Projects/ComdisAI/ComdisAI.Tests/ComdisAI.Tests.csproj` to confirm existing behavior remains intact.
3. Review the rendered layout with an active role that has all relevant permissions: Sales and Procurement appear once each, contain the links in the specified order, and Catalogs remains unchanged.
4. Repeat with role combinations: sales-only, procurement-only, each single child permission, and no transaction permissions. Parent groups must appear only when at least one child is available, and unauthorized children must not render.
5. On desktop and collapsed mobile navigation, open both dropdowns and click every destination to confirm links still resolve to each controller's Index action.
6. Search the final layout for direct top-level Sales Orders, Sales Reports, Customer Payments, Purchase Orders, and Supplier Payments nav items; only their grouped dropdown entries should remain.

**Decisions**
- Interpret `Procurment` as the correctly spelled UI label `Procurement`.
- Interpret `Suppliers Playments` as the existing feature/UI term `Supplier Payments` and retain the `SupplierPaymentsController` route.
- Catalogs is explicitly out of scope: no child movement, renaming, authorization change, or markup reordering inside that group.
- No controllers, authorization actions, database schema, migrations, or route changes are required.
- No new automated UI-test dependency is planned for this narrow markup regrouping; build/tests plus a permission-and-responsive manual matrix provide focused verification.
- Plan-mode constraint: no repository branch or code changes were created during planning; branch creation is step 1 of the executable plan.
