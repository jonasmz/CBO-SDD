# Data Model: Central Authentication and Authorization Service

Persistence types live in Infrastructure. Only the entities marked **Domain** exist in the Domain
project (they carry real behavior); the rest are Identity/EF persistence shapes.

## User (Infrastructure: `ApplicationUser : IdentityUser<Guid>`)

| Field | Notes |
|---|---|
| `Id` (Guid) | Stable unique identifier, the JWT `sub` |
| `Email` / `NormalizedEmail` | Login identifier; unique (case-insensitive via normalization) |
| `UserName` | Set equal to email internally; never shown or required from users |
| `PasswordHash` | Identity hasher output; never exposed |
| `IsEnabled` (bool) | Application state; `false` blocks login, refresh and token issuance |
| `CreatedAt` (UTC) | Audit |
| `SecurityStamp`, `LockoutEnd`, `AccessFailedCount` | Identity internals (lockout, reset-code invalidation) |

Custom user class is justified by `IsEnabled` and `CreatedAt`. The built-in `LockoutEnabled` is not
reused for administrative disable, so lockout and disable stay independent.

Rules:
- Email unique; creation with duplicate email is rejected (409).
- At least one role at all times; creation requires one or more roles (FR-004).
- Password rule applies on creation, reset and bootstrap: 8+ characters, upper, lower, digit (FR-015). It is
  defined once in Domain `PasswordPolicy` (real domain behavior: validates a candidate password and
  returns the violated rules, never the password); Identity options mirror it.
- Disabled or locked-out users fail sign-in; this decision is made by the Login handler from facts
  reported by the credential verifier, not by the adapter.

## Role (Identity `IdentityRole<Guid>`; names from Domain `AppRoles`)

Exactly three rows, seeded idempotently at startup: `Administrator`, `Manager`, `Operator`.
**Domain**: `AppRoles` holds the canonical names and `TryParse(string, out role)`; every role route
parameter and request is validated through it. No API creates, renames or deletes roles.

## User–Role (Identity `IdentityUserRole<Guid>`)

Many-to-many; a user may hold several roles.

Invariants enforced in the Application slices (using counts from the `IUserDirectory` port):
- RemoveRole refuses removal of a user's last role.
- RemoveRole and DisableUser refuse changes that would leave zero enabled Administrators.

## RefreshToken (**Domain** behavior, mapped by Infrastructure)

| Field | Notes |
|---|---|
| `Id` (Guid) | Row identity |
| `FamilyId` (Guid) | Identifies the session; shared by all tokens produced by rotation |
| `UserId` (Guid) | Owner |
| `TokenHash` (string, unique index) | Base64 SHA-256 of the opaque token; the raw value is never stored |
| `CreatedAt`, `ExpiresAt` (UTC) | `ExpiresAt` = min(created + individual lifetime, `SessionExpiresAt`); individual lifetime 7 days by default (configurable) |
| `SessionStartedAt`, `SessionExpiresAt` (UTC) | Set once at sign-in and copied unchanged to every token of the family on rotation; `SessionExpiresAt` = `SessionStartedAt` + absolute session lifetime (30 days by default, configurable) |
| `State` | `Active`, `Rotated`, `Revoked` |
| `ReplacedByTokenId` (Guid?) | Set on rotation |
| `RevokedAt`, `RevokedReason` | `Logout`, `ReuseDetected`, `UserDisabled`, `PasswordReset` |

State transitions (implemented as methods on the Domain type, no framework dependency):

```text
Active --Rotate(now)--> Rotated        (only if the token and the session are not expired; produces a new Active
                                        token, same family, same SessionExpiresAt, ExpiresAt capped at it)
Active --Revoke(reason)--> Revoked
Rotated --presented again--> family revoked (all tokens in FamilyId become Revoked: ReuseDetected)
Revoked --presented--> rejected (family already revoked)
Any token of a session past SessionExpiresAt --presented--> rejected as expired session (checked first; no family revocation work, even for a Rotated token)
Expired Active token --presented--> rejected (user must sign in with credentials)
```

Family-level operations: revoke by `FamilyId` (logout, reuse), revoke by `UserId` (disable, reset).
Row retention: rows persist until expiry plus a documented cleanup, so reuse can be detected.

## PasswordReset (no table)

Uses Identity's data-protection-based reset token (24-hour lifespan). Single-use because redeeming changes
the security stamp. Depends on persisted Data Protection keys (`/data/keys`).

## Configuration entities (Options, not persisted)

`JwtOptions` (issuer, audience, access-token minutes, signing key source, previous public keys),
`SessionOptions` (refresh-token days, absolute session days; startup requires session days >= refresh-token days), `BootstrapOptions` (admin email/password), `CorsOptions`,
`DatabaseOptions` (path, data-protection keys path, migrate-on-startup), `IdentityAccountOptions` (reset-code hours, lockout), `RateLimitOptions`.

## Indices and constraints (Infrastructure)

- Unique on `AspNetUsers.NormalizedEmail`
- Unique on `RefreshTokens.TokenHash`
- Index on `RefreshTokens(FamilyId)` and `RefreshTokens(UserId)`
- Foreign keys from `RefreshTokens.UserId` to users with cascade delete (user deletion is not an API
  operation, so this only matters for administrative database repair)
