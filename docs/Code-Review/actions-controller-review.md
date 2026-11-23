# ActionsController Code Review

## Summary

`ActionsController` handles trimming, anti-forgery protection, action-level authorization, case-insensitive uniqueness, and duplicate insert races consistently with the surrounding dictionary controllers. The main risks are related to authorization integrity and soft-delete behavior because action codes are security configuration rather than ordinary dictionary values.

## 🔴 Security: Authorization identities are mutable

**Location:** `ComdisAI/Controllers/ActionsController.cs`, Edit action, lines 88-89

The `Code` property directly determines the permission granted by `ActionPermissionService`. A user with `EDIT_ACTION` can change permission codes between records and grant another role a stronger permission without having `MANAGE_ROLE_ACTIONS`.

For example, an existing action could be renamed, allowing another assigned action to adopt a code such as `DELETE_CUSTOMER` and changing that role's effective permissions.

**Suggestion:**

- Mark seeded or application-required actions as system actions.
- Prevent changing the `Code` of system actions.
- Allow unrestricted code editing only for explicitly defined custom actions.

## 🔴 Required actions can be deleted

**Location:** `ComdisAI/Controllers/ActionsController.cs`, Delete action, lines 107-116

The controller permits deletion of any seeded action, including permissions such as `VIEW_ACTION`, `EDIT_ACTION`, and `DELETE_CUSTOMER`. `AuthorizationSeeder` throws during startup when an application-required action is logically deleted.

This can leave the application unable to start after an authorized administrator deletes a required action.

**Suggestion:**

- Prevent deletion of required or system actions.
- Introduce a persistent `IsSystem` property or equivalent classification.
- Keep the restriction in the controller and, where appropriate, enforce it again in the data/service layer.

## 🟡 Soft-deleted actions remain addressable by ID

**Location:** `ComdisAI/Controllers/ActionsController.cs`, Edit and Delete actions, lines 53-58 and 108-111

The controller retrieves records through `GetByIdAsync`, which uses EF Core's `FindAsync`. `FindAsync` does not apply global query filters, so a caller who knows the ID of a soft-deleted action can retrieve and update or repeatedly delete it.

**Suggestion:**

Query by ID through a filtered LINQ query, for example:

```csharp
var action = await dbContext.Actions
    .SingleOrDefaultAsync(action => action.Id == id, cancellationToken);
```

Alternatively, explicitly reject records for which `IsDeleted` is `true`.

## 🟡 Missing controller-specific test coverage

There are tests for the authorization service, but no dedicated `ActionsController` tests covering important behavior:

- Soft-deleted IDs return `NotFound`.
- System action codes cannot be changed.
- System actions cannot be deleted.
- Create and edit requests trim values.
- Duplicate codes are rejected case-insensitively, including soft-deleted records.
- Uniqueness race failures return a validation error without persisting invalid state.
- Permission changes remain consistent after an action is edited or deleted.

## 💭 Immediate deletion has no confirmation step

The Actions Index view submits the Delete form immediately. Because deletion revokes effective permissions, an explicit confirmation step would reduce accidental changes.

## Positive Observations

- POST actions use anti-forgery validation.
- Every controller action is protected by the appropriate authorization action.
- Input is normalized before validation.
- Duplicate checks include soft-deleted records, respecting the normalized unique index.
- `DbUpdateException` handling covers concurrent uniqueness violations.
- Model binding is limited to editable fields.
- Cancellation tokens are propagated to asynchronous database operations.

## Recommended Order of Work

1. Make required authorization codes immutable.
2. Prevent deletion of required authorization actions.
3. Ensure Edit and Delete ignore soft-deleted records.
4. Add focused controller and authorization tests for these scenarios.
5. Add a delete confirmation interaction if it matches the application UX.
