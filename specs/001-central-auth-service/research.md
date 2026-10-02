# Research: Central Authentication and Authorization Service

No `NEEDS CLARIFICATION` remained after the clarification sessions. This document records the
technical decisions the plan depends on. Items marked **VERIFY** depend on package behavior that could
not be confirmed from documentation during planning (no .NET toolchain is available outside Docker)
and MUST be checked inside the dev container during implementation.

## R1. Signing algorithm and key mechanism

- **Decision**: RS256 with an RSA key (2048-bit minimum) supplied through configuration. Two accepted
  sources: a PEM private key file path, or a PFX/X.509 certificate path plus password. Exactly one source
  must be configured. `kid` = base64url SHA-256 thumbprint of the public key.
  Protection (spec FR-034): read-only mount into the service container; readable only by the runtime user
  and the provisioning operator (owner operator, group = container runtime GID, mode 0640, directory
  0750); never in an image or repository; a PFX password only through external secret configuration
  (never a literal in a Dockerfile or Compose file, never logged); a missing or invalid PFX file or
  password fails Production startup with a clear, secret-free message; PEM keys follow the same rules;
  throwaway local validation keys are exempt.
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
  Emergency replacement (spec FR-035) is different: the compromised public key is removed from
  `PreviousPublicKeys` immediately (no overlap) together with a new `Current` key and a restart.
  Consumers using the key endpoint replace their cached set on every refresh and adopt the new key on an
  unknown `kid`; the removed key is dropped within 5 minutes. Static-key consumers keep accepting the old
  key until updated by hand or automation (documented limitation). Trust in the old key outranks the
  validity of already-issued access tokens; sessions are not destroyed automatically.
- **Rationale**: Satisfies FR-010 and the "no source change" rotation requirement; a `kid` header lets
  consumers pick the right key during overlap.
- **Alternatives**: full OIDC discovery document (out of scope: no OIDC).

## R3. Access token content

- **Decision**: claims exactly `sub` (user id), `role` (one claim per role), `iss`, `aud`, `iat`, `exp`
  (spec FR-007). No `jti`, no `nbf`, no email; consumers needing the email call `/api/me`. Lifetime 15 minutes
  (`Jwt:AccessTokenMinutes`). Claim names are not remapped (`MapInboundClaims = false`) with
  `RoleClaimType = "role"` and `NameClaimType = "sub"` on both the service and documented consumers.
- **Rationale**: Minimal claims (Constitution V); email can change, tokens should not carry mutable data.

## R4. Refresh-token design

- **Decision**: opaque 32-byte random value, base64url; stored as SHA-256 hash with `FamilyId`,
  `ExpiresAt`, `SessionStartedAt`, `SessionExpiresAt`, `State` (Active, Rotated, Revoked),
  `ReplacedByTokenId`. Individual lifetime 7 days (fresh on rotation); absolute session lifetime 30 days
  from the original sign-in; a rotated token's `ExpiresAt` is capped at `SessionExpiresAt`, so a session
  is sliding but bounded; defaults configurable, absolute must be >= individual, validated at startup
  (individual 1 to 90 days, absolute 1 to 365 days). Rotation inside a transaction.
  Reuse of a non-Active token revokes every token in the family. Expired rows retained until a documented
  cleanup (manual SQL or later task).
- **Rationale**: Matches clarified rules; hashing means a database leak does not yield usable tokens.
  A fast hash is appropriate because the input has 256 bits of entropy.
- **Concurrency**: two simultaneous refreshes with the same token; SQLite serializes writes and the
  second sees `Rotated`, which triggers family revocation. Accepted: a client that races itself is signed
  out.

## R5. Login without account enumeration

- **Decision**: one generic 401 ProblemDetails for unknown email, wrong password, disabled account, and
  lockout. The `ICredentialVerifier` adapter only reports facts (user found, password valid, locked out,
  enabled, roles); for an unknown email it performs verification against a fixed dummy hash so processing
  time does not reveal existence. The Login handler makes the decision (fail unless found, valid,
  enabled and not locked out). Identity lockout counters (5 failures, 15 minutes, configurable) provide
  per-account throttling; the ASP.NET rate limiter (per source address, 10 requests/minute, configurable)
  covers login, refresh and reset-password and returns 429.
- **Rationale**: Spec FR-005, FR-021; constitution I (decisions not in adapters).
- **Alternatives**: stronger timing guarantees are explicitly not promised (spec FR-005).

## R6. Password policy and reset

- **Decision**: the rule lives once, in Domain `PasswordPolicy` (length 8, upper, lower, digit, no
  non-alphanumeric requirement), applied by the CreateUser, ResetPassword and BootstrapAdministrator
  handlers; Identity `PasswordOptions` mirror the same constants as defense in depth. Reset via
  `UserManager.GeneratePasswordResetTokenAsync` behind `IPasswordResetCodes`, token lifespan from
  `Identity:ResetCodeHours` (default 24, allowed 1 to 72, `DataProtectionTokenProviderOptions`),
  single-use because the redemption changes the security stamp. Data Protection keys persist at
  `Database:DataProtectionKeysPath` (default `/data/keys`).
- **Disabled accounts**: issuing a code for a disabled account is allowed; the ResetPassword handler
  validates the password first (400), then checks the account state through `IUserDirectory` and answers a
  disabled account with the same generic 401 as an invalid code without consuming the code. Re-enabling
  does not sign the user in.
- **Rationale**: Clarified rules, no email infrastructure, no custom crypto.
- **Risk**: if the key ring is lost, outstanding reset codes become invalid; documented in operations.

## R7. First Administrator bootstrap

- **Decision**: the Application slice `BootstrapAdministrator` holds the rule (allowed only when
  `IUserDirectory` reports zero users and valid credentials were provided, creating the user through
  `IUserCreator`, which writes user and role in one transaction). A hosted service in Api calls it after
  migrations and role seeding and logs the outcome. Variables `Bootstrap__AdminEmail`,
  `Bootstrap__AdminPassword`. After any user exists the slice performs no write and the configuration is
  ignored. Operators confirm via the log line `Bootstrap skipped: users already exist` and by restarting
  with different values.
- **Rationale**: Simplest secure behavior; no always-open endpoint exists.
- **Alternatives**: one-time token endpoint (adds an unauthenticated write surface); CLI command (needs
  exec access to the container).

## R8. Persistence and migrations

- **Decision**: EF Core with SQLite file on a named volume. Migrations authored with
  `docker compose run --rm dev dotnet ef migrations add ...` using a design-time context factory.
  Runtime application via the `--migrate` switch of the published image (no EF CLI needed in
  production; it builds only the database services, so it needs no signing key) and a `migrate` compose
  service. A normal start fails clearly when the schema is pending and then seeds roles idempotently. No `EnsureCreated`, no automatic destructive action.
  Email uniqueness enforced by Identity (`RequireUniqueEmail`) plus a unique index on normalized email.
- **Rationale**: Constitution VI and X; production image stays free of SDK tooling.
- **Backup**: documented as stop service then copy the volume file, or SQLite online backup using
  `sqlite3 .backup` through the repository-built `tools` service (never an ad-hoc public image).

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
  target remains (objective acceptance for SC-008: the production page has no send/execute control, no
  server URL and no credential input, checked in the Phase 6 validation task). The Production OpenAPI
  document contains no secrets, example tokens or internal URLs.
- **Rationale**: Constitution VIII explicitly allows an equivalent configuration when exact behavior is
  not guaranteed. Server-side authorization remains the real protection.

## R10. Rate limiting, CORS, health (spec FR-021, FR-032, FR-033)

- **Decision**: built-in `AddRateLimiter` fixed-window policies by remote IP (default 10 requests per
  minute, configurable, spec FR-021) for `/api/auth/login`, `/api/auth/refresh`,
  `/api/auth/reset-password`. CORS origins from
  `Cors:AllowedOrigins`; with an empty list, no cross-origin access in Production. Behind a reverse
  proxy, forwarded headers are enabled by configuration only. `GET /health` returns 200/503 with a body
  of just `Healthy`/`Unhealthy` and checks database connectivity; it is anonymous and leaks nothing.
- **Rationale**: Spec FR-021; constitution XII.

## R11. Startup validation

- **Decision**: options classes bound with `ValidateDataAnnotations` and custom validators,
  `ValidateOnStart`. Production requires: signing key configured and loadable, issuer, audience,
  RSA key at least 2048 bits, a PFX file and password that load when a PFX is configured, session
  lifetimes with absolute >= individual, CORS list parseable, and bootstrap password when the store is empty.
  `Database:MigrateOnStartup=true` is rejected outside Development. Startup logs the signing key
  identifier and, for certificates, the expiry date (spec FR-031); validity dates are not enforced.
  Development may auto-generate a throwaway RSA key under `/data/dev-keys`, never active when
  `ASPNETCORE_ENVIRONMENT=Production`.

## R12. Docker layout

- **Decision**: `Dockerfile` multi-stage (`sdk:10.0` publish, `aspnet:10.0` run, non-root `app` user,
  `/data` created and owned by that user before `VOLUME`, `HEALTHCHECK` through a `--healthcheck` switch
  of the published app because the runtime image has no curl). `Dockerfile.dev` based on `sdk:10.0` with
  `dotnet-ef` installed, `/data` and `/nuget` world-writable so any `LOCAL_UID` can write to new named
  volumes, source bind-mounted from `${HOST_PROJECT_DIR:?}`. `Dockerfile.tools` is a pinned Alpine image
  with sqlite3, openssl and curl. Compose services: `dev` (tooling and tests), `dev-api` (runs the app
  from source in Development, profile `run`), `tools`, `auth-api` (runtime image, Production default),
  `migrate` (one-shot, same image and volume as `auth-api`). Volumes have explicit names:
  `authservice-dev-data` (dev, `dev-api`, `dev`), `authservice-data` (production), `nuget-cache`. Host
  published ports are only for humans (`DEV_API_PORT` default 8081, `API_PORT` default 8080) and differ
  so both can run. Key material is supplied through a read-only bind mount of `${SIGNING_KEY_DIR}` and
  never copied into an image.
- **Rationale**: Principles IX and X; the agent container reaches the service only through the compose
  network (see R14).

## R13. Testing approach

- **Decision**: `AuthService.IntegrationTests` using `WebApplicationFactory<Program>` with a temp-file
  SQLite database per fixture, a generated RSA key, and seeded users per role. Planned tests, grouped in 7
  files: login success, login failure, disabled login (LoginTests); token signature + issuer/audience via
  an independent `JwtBearer` validation using only the public key, role claims present (JwtTests); admin
  allowed, manager and operator forbidden (AuthorizationTests); assign supported role, reject
  unsupported role, last-role and last-admin guards (RoleAssignmentTests); refresh rotation, rotated-token
  reuse revokes family, logout revokes session, password-reset redemption revokes sessions
  (RefreshTokenTests); bootstrap only on an empty store (BootstrapTests); persistence round trip after a
  host restart (PersistenceTests).
  `AuthService.ArchitectureTests` has four reference checks. Nothing else.
- **Rationale**: Constitution XI and the validation gate.
- **Package versions**: pin the latest .NET 10 compatible versions at implementation time; record the
  resolved versions in `Directory.Packages.props`.

## R14. Smoke validation from an agent container

- **Decision**: all HTTP checks run inside the compose network with
  `docker compose run --rm tools curl http://dev-api:8080/...` (or `http://auth-api:8080`), and database
  inspection with `docker compose run --rm -v authservice-dev-data:/data:ro tools sqlite3 ...`.
  Readiness before the health endpoint exists (Phase 3 to 5) polls `/.well-known/jwks.json`; afterwards
  `/health`. Services are started detached.
- **Rationale**: the agent container uses the host Docker daemon, so host-published ports and host paths
  are not reachable or resolvable from the agent's own network and filesystem namespace. `HOST_PROJECT_DIR`
  must be the repository path as seen by the host daemon; it is found with `docker inspect` of the agent
  container's mounts and documented in `docs/development.md`.
