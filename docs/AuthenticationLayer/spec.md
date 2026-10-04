# Authentication Layer

Implement database-backed authentication for local user accounts. Store password hashes using ASP.NET Core's password hasher; never store or display plaintext passwords or hashes. Successful sign-in issues an authentication cookie containing the user's ID, name, and email. Update the last-login timestamp only after a successful password verification.

Provide a user dictionary with list, create, edit, logical delete, and password-reset screens. New and reset passwords must be at least 8 characters and confirmed before saving. Normalize email addresses by trimming and comparing without regard to case; email addresses remain reserved after logical deletion.

## User table

| Column | Data type | Nullable | Comments |
|--------|-----------|----------|----------|
| Id | int | false | Primary key; autoincrement |
| Name | nvarchar(255) | false | |
| Surname | nvarchar(255) | false | |
| Email | nvarchar(255) | false | Unique without regard to case |
| PasswordHash | nvarchar(255) | false | ASP.NET Core password hash |
| LastLoginDateUtc | datetime | true | Null until the first successful sign-in |
| IsDeleted | bool | false | Logical deletion |
| DeletedAtUtc | datetime | true | Logical deletion timestamp |
| DeletedBy | nvarchar | true | Actor who logically deleted the user |
| CreatedAtUtc | datetime | false | Shared audit field |
| CreatedBy | nvarchar | false | Shared audit field |
| UpdatedAtUtc | datetime | false | Shared audit field |
| UpdatedBy | nvarchar | false | Shared audit field |

The database also stores a normalized email value for its unique index. Queries hide logically deleted users. The `Delete` operation must never physically remove a user.

## Scope boundary

This layer authenticates users but does not authorize them. It does not add roles, policies, or route protection; existing screens and user-management actions remain accessible without signing in until the authorization layer is implemented.
