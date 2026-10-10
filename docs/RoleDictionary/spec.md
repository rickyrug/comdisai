# Roles dictionary

Roles are maintained through an MVC screen and are available for use by the
authorization layer. Users can list, create, edit, and logically delete roles.
Deleting a role sets its deletion metadata and hides it from normal role
queries; it does not physically remove it or its action assignments.

## Role table

| Column | Data type | Nullable | Comments |
| --- | --- | --- | --- |
| Id | int | false | Primary key, auto-increment |
| Code | nvarchar(50) | false | Unique, case-insensitive after trimming |
| Description | nvarchar(255) | false | |
| IsDeleted | bit | false | Logical deletion flag |
| DeletedAtUtc | datetime | true | UTC deletion timestamp |
| DeletedBy | string | true | Authenticated actor identifier |
| CreatedAtUtc | datetime | false | UTC audit timestamp |
| CreatedBy | string | false | Authenticated actor identifier |
| UpdatedAtUtc | datetime | false | UTC audit timestamp |
| UpdatedBy | string | false | Authenticated actor identifier |

The role screen supports creating and editing the code and description.
Uniqueness applies to deleted roles as well, so a deleted code remains
reserved.

## Role-action assignments

The role detail screen lists assigned actions and allows assigning an available
action or removing an assignment. Removing an assignment physically deletes
only its relationship row. Assignments to logically deleted actions remain
stored and are shown as deleted so they can be removed.

| Column | Data type | Nullable | Comments |
| --- | --- | --- | --- |
| Id | int | false | Primary key, auto-increment |
| idRole | int | false | Foreign key to `Role.Id` |
| idAction | int | false | Foreign key to `Action.Id` |
| CreatedAtUtc | datetime | false | UTC time the permission was assigned |
| CreatedBy | string | false | Actor identifier; `system` if unauthenticated |
| UpdatedAtUtc | datetime | false | UTC audit timestamp |
| UpdatedBy | string | false | Actor identifier; `system` if unauthenticated |

Each role/action pair is unique. An action may be assigned to multiple roles;
one role may have multiple actions. Removing an assignment physically deletes
its row. Existing assignments receive `unknown` actor values and the migration
time as their audit timestamps because their original grant details are not
available.
