# Actions dictionary

Actions define the permission identities used by the authorization layer. The dictionary supports list, create, edit, and logical delete operations with case-insensitive uniqueness across active and deleted records.

## Table definition

| Column | Type | Nullable | Comments |
| --- | --- | --- | --- |
| `Id` | integer | false | Primary key and auto-increment |
| `Code` | text (50) | false | Case-insensitively unique through `NormalizedCode` |
| `Description` | text (255) | false | Editable display text |
| `IsSystem` | boolean | false | Identifies application-required authorization actions |
| `IsDeleted` | boolean | false | Logical-delete flag |
| audit fields | datetime/text | mixed | Shared UTC actor and timestamp metadata |

## Action classification

- Application-required codes from `AuthorizationActionCodes` are system actions. `AuthorizationSeeder` creates them with `IsSystem = true` and promotes matching legacy records rather than duplicating them.
- System action codes are immutable. Their descriptions may be edited, but changing the code or deleting the action is rejected in both the controller and `AppDbContext` persistence guards.
- Custom actions created through the UI have `IsSystem = false`. Their codes remain editable and they can be logically deleted.
- Deleted action codes remain reserved by the normalized unique index.

## Controller and UI behavior

- GET/POST Edit and Delete resolve IDs with the active-record query filter. Deleted and unknown IDs return `404`, including invalid POST Edit submissions.
- A system-code mutation produces a `Code` validation error without saving. A case-only submission is treated as unchanged and uses the stored code.
- Forged deletion of an active system action returns HTTP `409 Conflict` and does not change audit fields or role assignments.
- System codes render read-only and omit the Delete control. Custom deletion uses browser confirmation, while server-side checks remain authoritative.
- Concurrent uniqueness violations are handled as `Code` validation errors and failed tracked changes are detached so clean queries do not observe unsaved controller entities.

## Migration

Apply `SystemActionClassification` before startup against databases that predate `Actions.IsSystem`. The migration adds a required `false`-default column and backfills every required authorization code. Startup seeding then remains idempotent and fails if a required row is logically deleted.
