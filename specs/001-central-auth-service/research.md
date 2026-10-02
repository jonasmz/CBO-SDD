# Research: Central Authentication and Authorization Service

No `NEEDS CLARIFICATION` remained after the clarification sessions. This document records the
technical decisions the plan depends on. Items marked **VERIFY** depend on package behavior that could
not be confirmed from documentation during planning (no .NET toolchain is available outside Docker)
and MUST be checked inside the dev container during implementation.

## R1. Signing algorithm and key mechanism

- **Decision**: RS256 with an RSA key (2048-bit minimum) supplied through configuration. Two accepted
  sources: a PEM private key file path, or a PFX/X.509 certificate path plus password. Exactly one source
  must be configured. `kid` = base64url SHA-256 thumbprint of the public key.
- **Rationale**: Clarification says X.509 is permitted but not required. RS256 is universally supported
  by consumer JWT libraries. Supporting both sources costs one small loader and avoids forcing
  certificate infrastructure on operators.
- **Alternatives**: ES256 (smaller tokens, less universal tooling); certificate-only (rejected by
  clarification); symmetric HS256 (rejected by constitution).

## R2. Key rotation and consumer verification

- **Decision**: Config has one `Current` signing key and an optional list of `PreviousPublicKeys`
  (public PEM or certificate files). The service publishes all public keys at
  `GET /.well-known/jwks.json`. Consumers either fetch the JWKS or are given a static public key file.
  Rotation: add new key as Current, move the old public key to Previous, wait at least one access-token
  lifetime plus clock skew, then remove it.
- **Rationale**: Satisfies FR-010 and the "no source change" rotation requirement; a `kid` header lets
  consumers pick the right key during overlap.
- **Alternatives**: full OIDC discovery document (out of scope: no OIDC).

## R3. Access token content

- **Decision**: claims `sub` (user id), `role` (one claim per role), `iss`, `aud`, `iat`, `nbf`, `exp`,
  `jti`. Email is not included in the token; consumers needing it call `/api/me`. Lifetime 15 minutes
  (`Jwt:AccessTokenMinutes`). Claim names are not remapped (`MapInboundClaims = false`) with
  `RoleClaimType = "role"` and `NameClaimType = "sub"` on both the service and documented consumers.
- **Rationale**: Minimal claims (Constitution V); email can change, tokens should not carry mutable data.

## R4. Refresh-token design

- **Decision**: opaque 32-byte random value, base64url; stored as SHA-256 hash with `FamilyId`,
  `ExpiresAt`, `State` (Active, Rotated, Revoked), `ReplacedByTokenId`. Rotation inside a transaction.
  Reuse of a non-Active token revokes every token in the family. Expired rows retained until a documented
  cleanup (manual SQL or later task).
- **Rationale**: Matches clarified rules; hashing means a database leak does not yield usable tokens.
  A fast hash is appropriate because the input has 256 bits of entropy.
- **Concurrency**: two simultaneous refreshes with the same token; SQLite serializes writes and the
  second sees `Rotated`, which triggers family revocation. Accepted: a client that races itself is signed
  out.

## R5. Login without account enumeration

- **Decision**: one generic 401 ProblemDetails for unknown email, wrong password, disabled account, and
  lockout. For an unknown email, run a password verification against a fixed dummy hash to keep timing
  close. Identity lockout (5 failures, 15 minutes) provides per-account throttling; ASP.NET rate limiter
  (per-IP fixed window) covers login, refresh and reset-password endpoints and returns 429.
- **Rationale**: Spec FR-005, FR-021.
- **Alternatives**: identical timing guarantees are not claimed beyond best effort.

## R6. Password policy and reset

- **Decision**: Identity `PasswordOptions`: length 8, require upper, lower, digit, no non-alphanumeric
  requirement. Reset via `UserManager.GeneratePasswordResetTokenAsync`, token lifespan 24 hours
  (`DataProtectionTokenProviderOptions`), single-use because the redemption changes the security stamp.
  Data Protection keys persist at `/data/keys`.
- **Rationale**: Clarified rules, no email infrastructure, no custom crypto.
- **Risk**: if the key ring is lost, outstanding reset codes become invalid; documented in operations.

## R7. First Administrator bootstrap

- **Decision**: `BootstrapAdminService` (hosted, runs after the database check). Allowed only when the
  user table has zero rows. Variables `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword`. After any user
  exists it performs no write and ignores configuration. Operators confirm via a log line
  (`Bootstrap skipped: users already exist`) and by attempting a restart with different values.
- **Rationale**: Simplest secure behavior; no always-open endpoint exists.
- **Alternatives**: one-time token endpoint (adds an unauthenticated write surface); CLI command (needs
  exec access to the container).

## R8. Persistence and migrations

- **Decision**: EF Core with SQLite file on a named volume. Migrations authored with
  `docker compose run --rm dev dotnet ef migrations add ...` using a design-time context factory.
  Runtime application via the `--migrate` switch of the published image (no EF CLI needed in
  production) and a `migrate` compose service. No `EnsureCreated`, no automatic destructive action.
  Email uniqueness enforced by Identity (`RequireUniqueEmail`) plus a unique index on normalized email.
- **Rationale**: Constitution VI and X; production image stays free of SDK tooling.
- **Backup**: documented as stop service then copy the volume file, or SQLite online backup using
  `sqlite3 .backup` in a throwaway container.

## R9. OpenAPI and Scalar read-only production mode (**VERIFY**)

- **Decision**: Microsoft.AspNetCore.OpenApi generates `/openapi/v1.json` in all environments. Scalar is
  mapped in all environments. In Development: bearer security scheme present, test-request client
  enabled. In Production: the plan requires an equivalent read-only configuration, implemented in this
  order of preference, whichever the installed `Scalar.AspNetCore` version supports:
  1. a built-in Scalar option that hides the test-request button / client (confirm the option name in the
     installed package; search results did not confirm one);
  2. passing Scalar's JavaScript configuration `hideTestRequestButton` (and hiding the client picker)
     through the package's custom-configuration hook if exposed;
  3. a document transformer that, in Production, removes `servers` and security schemes from the OpenAPI
     document so no request target or credential input exists, combined with `WithProxy(null)`.
  The implementation MUST include a smoke check that confirms the production page loads and no executable
  target remains. The Production OpenAPI document contains no secrets, example tokens or internal URLs.
- **Rationale**: Constitution VIII explicitly allows an equivalent configuration when exact behavior is
  not guaranteed. Server-side authorization remains the real protection.

## R10. Rate limiting, CORS, health

- **Decision**: built-in `AddRateLimiter` fixed-window policies by remote IP for
  `/api/auth/login`, `/api/auth/refresh`, `/api/auth/reset-password`. CORS origins from
  `Cors:AllowedOrigins`; with an empty list, no cross-origin access in Production. Behind a reverse
  proxy, forwarded headers are enabled by configuration only. `GET /health` returns 200/503 with a body
  of just `Healthy`/`Unhealthy` and checks database connectivity; it is anonymous and leaks nothing.
- **Rationale**: Spec FR-021; constitution XII.

## R11. Startup validation

- **Decision**: options classes bound with `ValidateDataAnnotations` and custom validators,
  `ValidateOnStart`. Production requires: signing key configured and loadable, issuer, audience,
  RSA key at least 2048 bits, CORS list parseable, and bootstrap password when the store is empty.
  Development may auto-generate a throwaway RSA key under `/data/dev-keys`, never active when
  `ASPNETCORE_ENVIRONMENT=Production`.

## R12. Docker layout

- **Decision**: `Dockerfile` multi-stage (`sdk:10.0` publish, `aspnet:10.0` run, non-root user, volume
  `/data`, `HEALTHCHECK` via the health endpoint using a minimal approach compatible with the image, since
  the runtime image has no curl; alternatively health probing from Compose). `Dockerfile.dev` based on
  `sdk:10.0` with `dotnet-ef` installed, source bind-mounted, NuGet cache on a named volume. Compose
  services: `dev` (tooling and test runs), `auth-api` (runtime image, port 8080, volume), `migrate`
  (one-shot). Key material supplied via bind-mounted secrets directory, never copied into the image.
- **Rationale**: Principles IX and X.

## R13. Testing approach

- **Decision**: `AuthService.IntegrationTests` using `WebApplicationFactory<Program>` with a temp-file
  SQLite database per fixture, a generated RSA key, and seeded users per role. Planned tests (about 15):
  login success, login failure, disabled login, token signature + issuer/audience via an independent
  `JwtBearer` validation using only the public key, role claims present, admin allowed, manager and
  operator forbidden, assign supported role, reject unsupported role, last-role/last-admin guard, refresh
  rotation, rotated-token reuse revokes family, logout revokes session, password reset redeem, bootstrap
  only on empty store, and persistence round trip after a context re-creation.
  `AuthService.ArchitectureTests` has four reference checks. Nothing else.
- **Rationale**: Constitution XI and the validation gate.
- **Package versions**: pin the latest .NET 10 compatible versions at implementation time; record the
  resolved versions in `Directory.Packages.props`.
