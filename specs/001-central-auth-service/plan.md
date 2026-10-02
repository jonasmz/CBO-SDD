# Implementation Plan: Central Authentication and Authorization Service

**Branch**: `001-central-auth-service` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-central-auth-service/spec.md`

## Summary

A standalone identity authority: email/password sign-in via ASP.NET Core Identity, short-lived
RS256 JWT access tokens (15 min) carrying only `sub`, `role`, `iss`, `aud`, `iat`, `exp`, single-use rotating opaque
refresh tokens (7 days each, 30-day absolute session cap, hashed at rest, family revocation on reuse), a fixed Administrator /
Manager / Operator role set, Administrator-only user and role administration, startup bootstrap of
the first Administrator, and Scalar-based API reference that is interactive only in Development.
Hexagonal architecture with vertical slices on .NET 10 + EF Core + SQLite; everything is built,
tested, migrated and run through Docker Compose (no local .NET). Technical choices and their
rationale are in [research.md](research.md).

## Technical Context

**Language/Version**: C# 14 on .NET 10 (SDK and ASP.NET runtime container images, tag `10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs, ASP.NET Core Identity, EF Core + SQLite provider,
`Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`,
`Microsoft.IdentityModel.JsonWebTokens` (Infrastructure only)

**Storage**: SQLite file `/data/auth.db` on a named Docker volume (`authservice-data` in production,
separate `authservice-dev-data` for development tooling); ASP.NET Data Protection keys on the same
volume (`/data/keys`, option `Database:DataProtectionKeysPath`) so reset codes survive container
replacement

**Testing**: xUnit; `Microsoft.AspNetCore.Mvc.Testing` integration tests against real SQLite; a few
`NetArchTest.Rules` boundary checks. No coverage target (Constitution XI)

**Target Platform**: Linux container (Docker / Docker Compose), single node

**Project Type**: web-service (single deployable API, four layered projects)

**Performance Goals**: sign-in under 2 s under normal load (SC-001); no further targets

**Constraints**: all `dotnet` operations execute in containers; private key never in image, repo or
logs; production start fails on invalid security configuration; SQLite single-writer (acceptable
for single node)

**Scale/Scope**: tens to low thousands of users, 2 consumer APIs initially, single instance

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design (see bottom row of each).*

| Principle | Status | How the plan satisfies it |
|---|---|---|
| I. Hexagonal + Vertical Slices | PASS | Four projects with inward references; features under `Application/Features/*`; endpoint mapping per feature in Api; boundaries enforced by architecture tests. Business decisions (disabled/locked users fail, bootstrap only on empty store, duplicate email, password rule, last-admin/last-role guards) live in Domain or Application; adapters return facts and perform mechanics only |
| II. Simple, explicit design | PASS | No MediatR, generic repository, UoW, event bus. Slice handlers are plain classes. Narrow, role-specific ports (interface segregation): `ICredentialVerifier`, `IUserDirectory`, `IUserCreator`, `IUserStateStore`, `IUserRoleStore`, `IPasswordResetCodes`, `IAccessTokenIssuer`, `IRefreshTokenStore`, `IUserSessionRevoker` |
| III. Centralized authentication | PASS | Identity password hashing and lockout counters behind `ICredentialVerifier`; the Login handler applies the generic-failure decision; dummy hash work for unknown email |
| IV. Fixed roles | PASS | `AppRoles` canonical definition in Domain; idempotent seed; role route parameter validated against it; authorization policy `RequireAdministrator` |
| V. Token security | PASS | RS256, externally supplied key (PEM or PFX), JWKS endpoint + public key file for consumers, explicit iss/aud/sig/lifetime validation |
| VI. Persistence as infrastructure | PASS | EF/Identity types only in Infrastructure; migrations only; no `EnsureCreated`; migrations applied by explicit `--migrate` command |
| VII. Explicit API contracts | PASS | Request/response records per slice; Identity entities never serialized; ProblemDetails |
| VIII. OpenAPI and Scalar | PASS (verify) | Production behavior verified against the installed package in the implementation phase, with a fallback that removes the execution target (research R9) |
| IX. Container-first | PASS | `dev` Compose service owns all dotnet work; a repository-built `tools` service (pinned base image) provides sqlite3, openssl and curl so no ad-hoc public images or agent-side tools are needed; agent container never runs `dotnet` |
| X. Docker reproducibility | PASS | Multi-stage `Dockerfile`, `Dockerfile.dev`, `Dockerfile.tools`, `compose.yaml`, `.dockerignore`, named volumes with explicit names; host-path variable `HOST_PROJECT_DIR` required (fail-fast) when the agent runs in a container |
| XI. Minimal testing | PASS | Two test projects, 7 integration test files plus one architecture test file, validation-gate coverage only, no coverage target |
| XII. Config, secrets, maintainability | PASS | Options pattern + `ValidateOnStart`; nullable enabled repo-wide via `Directory.Build.props`; secrets via env/mounted files |

**Deviation from the suggested structure**: `tests/AuthService.Application.Tests/` is not created.
Application handlers are thin orchestration over Identity/EF; their risky behavior (rotation, reuse
detection, last-admin guard) is verified through the integration tests. A pure-logic unit project is
added only if a handler grows branching worth isolating. Recorded here per Constitution Principle II.

Post-design re-check: PASS, no violations; Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-central-auth-service/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── http-api.md
│   └── token-and-jwks.md
└── tasks.md             # created later by /speckit-tasks
```

### Source Code (repository root)

```text
Directory.Build.props            # nullable enable, analyzers, warnings-as-errors for security rules
global.json                      # pins the .NET 10 SDK
AuthService.slnx
Dockerfile                       # multi-stage: sdk:10.0 build/publish -> aspnet:10.0 runtime, non-root numeric APP_UID:APP_GID (fallback ARG defaults only)
Dockerfile.dev                   # sdk:10.0 + dotnet-ef tool; /data writable for any UID
Dockerfile.tools                 # pinned alpine + sqlite3, openssl, curl for validation/smoke steps
compose.yaml                     # services: dev, dev-api, tools, auth-api, migrate;
                                 # volumes: authservice-dev-data, authservice-data, nuget-cache
.dockerignore
.env.example                     # documented variables, no secrets
README.md
docs/
├── architecture.md
├── development.md
├── authentication.md
├── authorization.md
├── jwt-consumer-integration.md
├── configuration.md
├── database.md
├── deployment.md
└── operations.md

src/
├── AuthService.Domain/
│   ├── Roles/AppRoles.cs                    # canonical names, TryParse
│   ├── Security/PasswordPolicy.cs           # 8 chars, upper, lower, digit; single source of the rule
│   └── Sessions/RefreshToken.cs             # rotation / revoke / reuse state machine
├── AuthService.Application/
│   ├── Abstractions/                        # narrow ports, each used by the slices that need it
│   │   ├── IAccessTokenIssuer.cs
│   │   ├── ICredentialVerifier.cs           # facts: found, password valid, locked out, enabled, roles
│   │   ├── IUserDirectory.cs                # read: by id, by email, list, counts
│   │   ├── IUserCreator.cs                  # atomic create of user with roles
│   │   ├── IUserStateStore.cs               # set enabled
│   │   ├── IUserRoleStore.cs                # get/add/remove role
│   │   ├── IPasswordResetCodes.cs           # issue and redeem reset codes
│   │   ├── IRefreshTokenStore.cs            # create, find, rotate, revoke family
│   │   └── IUserSessionRevoker.cs           # revoke all sessions of a user
│   └── Features/
│       ├── Bootstrap/BootstrapAdministrator/ # decides whether first Administrator is created
│       ├── Authentication/
│       │   ├── Login/                       # request, response, handler
│       │   ├── RefreshToken/
│       │   ├── Logout/
│       │   └── ResetPassword/               # redeem reset code (public)
│       ├── Identity/GetCurrentUser/
│       ├── Users/
│       │   ├── CreateUser/
│       │   ├── GetUser/
│       │   ├── ListUsers/
│       │   ├── EnableUser/
│       │   ├── DisableUser/                 # also revokes all sessions
│       │   └── IssuePasswordReset/          # administrator generates reset code
│       └── Roles/
│           ├── AssignRole/
│           └── RemoveRole/                  # refuses last role / last admin
├── AuthService.Infrastructure/
│   ├── Persistence/                         # AuthDbContext, entity configs, Migrations/
│   ├── Identity/                            # ApplicationUser, port adapters over UserManager (mechanics only), role seeding
│   ├── Tokens/                              # signing key loader, JWT issuer, JWKS builder
│   ├── Sessions/                            # refresh-token store (hashing), session revoker
│   └── DependencyInjection.cs
└── AuthService.Api/
    ├── Program.cs                           # composition root
    ├── Hosting/                             # bootstrap hosted service (calls the Application slice)
    ├── Features/<Feature>Endpoints.cs       # route groups, DTO binding, TypedResults
    ├── Security/                            # policies, rate limiting, CORS, options validation
    ├── OpenApi/                             # transformers, Scalar setup
    └── Properties/

tests/
├── AuthService.IntegrationTests/            # WebApplicationFactory + real SQLite + generated test key
└── AuthService.ArchitectureTests/           # four reference/boundary checks
```

**Structure Decision**: four `src` projects matching the constitution's boundaries, plus two test
projects. Ports in `Application/Abstractions` are small and role-specific (listed in the
Constitution Check, row II); one Infrastructure class may implement several of them. Adapters return
facts or perform mechanics (hashing, transactions, framework calls); every decision is made in a
handler or in Domain. A slice-private abstraction lives in its own slice folder. Application may use
`Microsoft.Extensions.Logging.Abstractions` for FR-031 event logging. No generic repository or unit of
work. The bootstrap hosted service lives in Api (host concern) and only calls the Application slice.

## Key Design Decisions (summary; detail in research.md)

- **Signing**: RS256 with an RSA key supplied by configuration, either a PEM private key file or a
  PFX certificate. `kid` is the key's thumbprint. Previous public keys are listed in configuration for
  rotation overlap and published, with the current one, at `GET /.well-known/jwks.json`.
- **Refresh tokens**: 32 random bytes, base64url; SHA-256 hash stored. A login creates a token family
  (session). Rotation marks the old token `Rotated` and issues a new one in the same family; reuse of a
  `Rotated` or `Revoked` token revokes the family. Logout revokes the presented token's family only.
  Disable and password reset revoke all of the user's families. Each token has an individual lifetime
  (default 7 days, fresh on every rotation) capped at the session's absolute expiration (default 30 days
  from the original sign-in, never extended by rotation); both configurable and validated at startup
  (`SessionOptions`, absolute >= individual; bounds are owned by spec FR-016). Absolute session expiration is
  checked first: a rotated token presented after it is rejected as an expired session without reuse-family
  revocation.
- **Bootstrap**: the Application slice `BootstrapAdministrator` decides: if the user count is zero and
  `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` are provided and satisfy `PasswordPolicy`, it
  asks `IUserCreator` to create the Administrator (user and role in one transaction); if any user exists
  it does nothing. A hosted service in Api invokes it after migrations and role seeding, logs the
  outcome, and in Production fails startup when the store is empty and no bootstrap values were given.
- **Password reset**: Identity password-reset token behind `IPasswordResetCodes` (data-protection based,
  lifespan `Identity:ResetCodeHours`, default 24, range 1 to 72, invalidated by the password change
  through the security stamp). Admin endpoint returns it once; user redeems it at a public endpoint.
  Redemption validates the new password with `PasswordPolicy` (400 first), refuses a disabled account with
  the same generic failure as an invalid code before consuming the code, and revokes all sessions.
  Issuance for a disabled account is allowed.
- **Password rule**: single source in Domain `PasswordPolicy`; Identity `PasswordOptions` mirror its
  constants as defense in depth. No lockout-based account-existence leak, because lockout failures
  return the same generic 401.
- **Signing-key protection** (spec FR-034): production key or PFX is read-only mounted, readable only by
  the runtime user and the provisioning operator, never in an image or repository. The runtime image runs as
  a non-root user with an explicit numeric identity defined by the project; no image-default UID is
  assumed. One operational source of truth: `APP_UID` and `APP_GID` in `.env` (documented defaults
  10001 and 10001 in `.env.example`; deployment configuration plays the same role outside Compose).
  `compose.yaml` passes them, without hard-coded fallbacks, to the image build arguments and to the
  `user:` of `auth-api` and `migrate`; the Dockerfile keeps `ARG APP_UID=10001`/`APP_GID=10001` only as
  safe fallbacks for direct `docker build` runs, and `docker image inspect` proves the image user equals
  the configured values. `LOCAL_UID`/`LOCAL_GID` (default 1000 in `.env.example`; development containers and local validation
  helpers only) are an independent identity set: never used for the production image, its `user:` or its
  volume, and `APP_*` is never used for development containers; `.env` is authoritative and the `:-1000`
  fallbacks in Compose are only safe defaults. Host
  provisioning uses those numbers: owner operator, group = `APP_GID`, file mode 0640, directory mode 0750
  (setting the group needs operator privileges). Fallback where numeric ids cannot be relied on (rootless
  Docker, userns-remap, orchestrators): provision a runtime-readable secret through the deployment mechanism
  (orchestrator secret) or inject an operator-owned 0600 secret into a container-visible location owned by
  the runtime user before startup; never run the container as root to read the key. A PFX password comes
  only from external secret configuration (production prefers mounted secret files via an optional
  key-per-file configuration source at `/run/secrets/config`, or the platform's secret mechanism; `.env` with
  secrets must be mode 0600 and untracked; environment variables can be visible through container
  inspection, an accepted limitation), never a literal in a Dockerfile or Compose file; missing or
  invalid PFX credentials fail Production startup with a clear, secret-free message. Local throwaway
  validation keys are exempt. Development containers use `LOCAL_UID`/`LOCAL_GID` on `authservice-dev-data`;
  production uses `APP_UID`/`APP_GID` on `authservice-data`; the two never share a volume.
- **Emergency key replacement** (spec FR-035): new `Current` key, compromised public key removed from the
  published set with no overlap, restart. JWKS consumers cache the key set for at most 5 minutes, replace
  (never merge) it on every successful refresh, and may force a refresh on an unknown `kid` at most once per
  minute per process; the removed key is dropped within 5 minutes after the service stopped publishing it, provided their refresh succeeds. The
  bound does not hold while a consumer cannot reach the key endpoint (it may keep its last fetched set), and
  static-key consumers keep accepting the old key until updated; both limitations are documented; no
  revocation service, introspection or per-request call.
- **Throttling** (spec FR-021): Identity lockout 5 failures/15 minutes (reported as a fact by
  `ICredentialVerifier`, decided by the Login handler) plus a per-source fixed window of 10 requests per
  minute on login, refresh and reset-password; both configurable.
- **Smoke validation networking**: the agent talks to the service through the `tools` container on the
  compose network (`http://dev-api:8080` or `http://auth-api:8080`), never through host-published ports.
- **Migrations**: explicit. The runtime image supports `--migrate` (database services only, no signing or
  bootstrap validation; apply migrations, seed roles, exit); compose provides a `migrate` service. A
  normal start never migrates unless `Database:MigrateOnStartup` is true (Development only); otherwise it
  fails with `Database schema is not up to date; run the migrate service` when migrations are pending,
  then seeds roles idempotently.
- **Endpoints added beyond the suggested list, required by clarified requirements**:
  `POST /api/users/{id}/password-reset`, `POST /api/auth/reset-password`,
  `GET /.well-known/jwks.json`.

## Complexity Tracking

No constitution violations; nothing to justify.
