# User Roles

## User-role assignment screen

Create a screen for assigning roles to users and removing role assignments.
The screen must:

1. Display the available users (not deleted) in a list and the available roles (not deleted) with checkboxes.
2. When a user is selected, check the boxes for all roles currently assigned to
   that user.
3. Allow roles to be assigned by checking their boxes and removed by unchecking
   their boxes. Removing an assignment physically deletes only its relationship
   row.
4. a save button must exist so the permision asigned or removed on a user can be saved in the table, should save in the database all the checked and remove the unchecked.

## User-role assignments

| Column | Data type | Nullable | Comments |
| --- | --- | --- | --- |
| Id | int | false | Primary key, auto-increment |
| idRole | int | false | Foreign key to `Role.Id` |
| idUser | int | false | Foreign key to `User.Id` |
| CreatedAtUtc | datetime | false | UTC time the role was assigned |
| CreatedBy | string | false | Actor identifier; `system` if unauthenticated |
| UpdatedAtUtc | datetime | false | UTC audit timestamp |
| UpdatedBy | string | false | Actor identifier; `system` if unauthenticated |

Each user-role pair is unique. A role may be assigned to multiple users, and a
user may have multiple roles. Existing assignments receive `system` as the
actor value and the migration time as their audit timestamps because their
original grant details are not available.

## Implementation clarifications

- The management screen is linked from each active user in the Users list. It
  lets an operator select any active user and edit that user's role checkboxes
  with one save action.
- Only active users and roles are offered. Assignments to soft-deleted roles
  are retained in storage and are not changed when saving the selected user's
  active roles.
- The new assignment table has a unique constraint on `(idUser, idRole)` and
  foreign keys to users and roles. Since this application does not currently
  have a user-role assignment table, the creation migration has no existing
  relationship rows to backfill.
