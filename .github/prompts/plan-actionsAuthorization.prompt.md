## Plan: Harden Actions authorization configuration

Validate and fix the review findings with persistent system-action classification, active-record lookup, defense-in-depth write guards, focused tests, and a small UX safeguard. Required authorization codes remain immutable and undeletable; custom actions retain existing edit/delete behavior.

**Steps**

### Phase 1: Define the system-action invariant
1. Add `Actions.IsSystem` as a required persisted boolean and configure it in `AppDbContext` for the `Action` table. Existing and newly seeded authorization codes owned by `AuthorizationActionCodes`/`AuthorizationSeeder` must be marked system; newly created UI actions default to false.
2. Make the required action definitions reusable instead of keeping them only in `AuthorizationSeeder.SeedActions`. Expose a read-only lookup that returns whether a normalized code is application-required, and make seeding set/promote `IsSystem = true` for matching rows. Keep case-insensitive normalization consistent with `NormalizedCode`.
3. Add an EF migration that adds `IsSystem` with default false and backfills all existing required authorization records by normalized required codes. Verify both `Up` and down migration behavior and ensure the migration handles an existing database before the seeder runs.
4. Add persistence-level invariant checks in `AppDbContext.ApplyAuditFields`: reject changing `Code` on an existing system action, reject setting/deleting `IsSystem` incorrectly, and reject soft-deleting a system action. Keep normal action audit updates intact. This is defense in depth; controller behavior remains responsible for returning friendly HTTP responses.

### Phase 2: Correct controller and view behavior (parallel with Phase 1 after `IsSystem` contract is agreed)
5. Replace `GenericRepository<Actions>.GetByIdAsync` usage in `ActionsController` with an `Actions` LINQ query that applies global filters (for example `dbContext.Actions.SingleOrDefaultAsync`). Apply this to GET Edit, POST Edit, and Delete. On POST Edit, load the active entity before validation so deleted/unknown IDs consistently return `NotFound`, including invalid form submissions.
6. In POST Edit, preserve the stored code for `IsSystem` records. If the submitted code differs after normalization, redisplay the form with a `Code` model error and no save; allow description changes for system actions. Continue trimming and using the filtered uniqueness check for custom actions.
7. In Delete, return `Conflict` (or an equivalent operation-not-allowed response chosen by the controller) for an active system action and do not mutate audit or role assignments. Active custom actions continue to soft-delete with existing audit behavior.
8. Update `_ActionForm` and `Edit`/`Index` presentation to make system codes read-only and hide/disable the delete control for system actions. Preserve the server-side checks because UI-only restrictions are bypassable.
9. Add the established lightweight browser confirmation to the custom-action Delete form (`onsubmit=confirm(...)`), matching existing views. This is independent of the authorization integrity protections and should not substitute for them.

### Phase 3: Focused automated coverage
10. Add `ComdisAI.Tests/ActionsDictionaryTests.cs` using the existing SQLite in-memory/AppDbContext/controller fixture pattern from `StatusDictionaryTests`. Cover create/edit trimming, case-insensitive duplicate rejection including soft-deleted codes, GET/POST Edit and Delete returning `NotFound` for deleted IDs, and custom-action soft-delete audit fields.
11. Add controller security tests for system actions: description can be edited, code mutation is rejected without persistence, forged deletion is rejected, and a matching-but-different-case submitted code is treated as unchanged. Add direct-context tests proving the `AppDbContext` invariant blocks unauthorized system code/delete mutations outside the controller.
12. Add seeder and migration tests: required rows all have `IsSystem = true` on fresh and repeated seed, a matching legacy row is promoted rather than duplicated, a logically deleted required row still prevents startup, and migrated databases receive/backfill the new column correctly.
13. Add an authorization regression test in `AuthorizationLayerTests`: editing a custom action code changes its permission identity as intended, while soft-deleting it removes the permission through the role-action/action global filters. Also verify seeded system actions cannot be repurposed to grant a different required permission.
14. Add a focused uniqueness-race test with a test double/interceptor that makes `SaveChangesAsync` throw a SQLite unique `DbUpdateException` after the controller's precheck. Assert a ViewResult with a `Code` validation error and that the duplicate is not present in a clean database query. Include both Create and Edit if the shared race handler can regress independently.

### Phase 4: Documentation and release note
15. Update the Actions layer specification/code-review note with the system/custom distinction, immutable required-code rule, filtered-ID behavior, and expected 409 behavior. Add a concise release note describing the migration and security hardening.

**Relevant files**
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Models/Actions.cs` — add persisted `IsSystem` classification.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Models/AuthorizationActionCodes.cs` — reference the authoritative required-code identities.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Data/AuthorizationSeeder.cs` — expose/reuse required definitions and mark/promote seeded records as system.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Data/AppDbContext.cs` — configure `IsSystem`, guard system-action writes in `ApplyAuditFields`, and retain soft-delete/audit filters.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Migrations/` — add generated `IsSystem` migration plus required-code backfill.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Controllers/ActionsController.cs` — filtered ID lookup, immutable system code handling, and protected Delete path.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Views/Actions/Index.cshtml` — hide/disable system delete and confirm custom deletion.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Views/Actions/Edit.cshtml` and `/Users/rickyrug/Projects/ComdisAI/ComdisAI/Views/Actions/_ActionForm.cshtml` — present system codes as read-only.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI.Tests/ActionsDictionaryTests.cs` — new controller/model/migration security and behavior coverage.
- `/Users/rickyrug/Projects/ComdisAI/ComdisAI.Tests/AuthorizationLayerTests.cs` — seeder and effective-permission regressions.
- `/Users/rickyrug/Projects/ComdisAI/docs/ActionsLayer/spec.md`, `/Users/rickyrug/Projects/ComdisAI/docs/Code-Review/actions-controller-review.md`, and `/Users/rickyrug/Projects/ComdisAI/docs/release-notes.md` — document decisions and outcome.

**Verification**
1. Run the focused tests with `dotnet test ComdisAI.Tests/ComdisAI.Tests.csproj --filter FullyQualifiedName~ActionsDictionaryTests` (or the repository's configured test-platform equivalent).
2. Run authorization tests with `dotnet test ComdisAI.Tests/ComdisAI.Tests.csproj --filter FullyQualifiedName~AuthorizationLayerTests`.
3. Run the full suite with `dotnet test ComdisAI.Tests/ComdisAI.Tests.csproj`; require no regressions.
4. Run `dotnet build ComdisAI/ComdisAI.csproj` to validate EF model, controller, and Razor views together.
5. Validate migration behavior against the test SQLite database: fresh schema, upgrade from the pre-`IsSystem` schema, system-code backfill, and repeated startup seeding.
6. Manual check: sign in with a role that has Edit/Delete Action permissions, confirm system code/delete controls are absent, forge POST requests to verify server rejection, and confirm custom actions still edit/delete normally.

**Decisions**
- The two red findings and the soft-deleted-ID finding are confirmed and are in scope.
- Use a persistent `IsSystem` field rather than deriving protection only from controller-time code strings, so the classification survives requests and database queries.
- Required/seeded actions: `Code` immutable and deletion prohibited; `Description` remains editable.
- Custom actions: normal create/edit/delete behavior remains, including intentional code changes.
- Keep the fix local to Actions rather than changing generic repository `GetByIdAsync` semantics across every dictionary entity; that broader behavior change has a separate blast radius.
- Uniqueness behavior remains case-insensitive across active and soft-deleted rows; the unique normalized index is authoritative.
- Delete confirmation is included as UX mitigation but is not considered an authorization control.

**Scope Exclusions**
- Do not restore or physically delete existing soft-deleted actions.
- Do not change other dictionary controllers' `FindAsync` behavior unless they receive their own reviewed fix.
- Do not redesign role-action management or add permission caching; current permission checks query the database per request.
- Do not change action authorization attributes or existing action-code constants.
