# Implementation Plan: Central Authentication and Authorization Service

**Branch**: `001-central-auth-service` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-central-auth-service/spec.md`

## Summary

A standalone identity authority: email/password sign-in via ASP.NET Core Identity, short-lived
RS256 JWT access tokens (15 min) carrying `sub` and `role` claims, single-use rotating opaque
refresh tokens (7 days, hashed at rest, family revocation on reuse), a fixed Administrator /
Manager / Operator role set, Administrator-only user and role administration, startup bootstrap of
the first Administrator, and Scalar-based API reference that is interactive only in Development.
Hexagonal architecture with vertical slices on .NET 10 + EF Core + SQLite; everything is built,
tested, migrated and run through Docker Compose (no local .NET). Technical choices and their
rationale are in [research.md](research.md).

## Technical Context

**Language/Version**: C# 14 on .NET 10 (SDK and ASP.NET runtime container images, tag `10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs, ASP.NET Core Identity, EF Core + SQLite provider,
`Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`,
`System.IdentityModel.Tokens.Jwt` / `Microsoft.IdentityModel.JsonWebTokens` (Infrastructure only)

**Storage**: SQLite file `/data/auth.db` on a named Docker volume; ASP.NET Data Protection keys on the
same volume (`/data/keys`) so reset codes survive container replacement

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
| I. Hexagonal + Vertical Slices | PASS | Four projects with inward references; features under `Application/Features/*`; endpoint mapping per feature in Api; boundaries enforced by architecture tests |
| II. Simple, explicit design | PASS | No MediatR, generic repository, UoW, event bus. Slice handlers are plain classes. Ports exist only where a handler needs them (see Structure Decision) |
| III. Centralized authentication | PASS | Identity `PasswordHasher` and `SignInManager`-style checks; uniform generic failure; dummy hash work for unknown email |
| IV. Fixed roles | PASS | `AppRoles` canonical definition in Domain; idempotent seed; role route parameter validated against it; authorization policy `RequireAdministrator` |
| V. Token security | PASS | RS256, externally supplied key (PEM or PFX), JWKS endpoint + public key file for consumers, explicit iss/aud/sig/lifetime validation |
| VI. Persistence as infrastructure | PASS | EF/Identity types only in Infrastructure; migrations only; no `EnsureCreated`; migrations applied by explicit `--migrate` command |
| VII. Explicit API contracts | PASS | Request/response records per slice; Identity entities never serialized; ProblemDetails |
| VIII. OpenAPI and Scalar | PASS (verify) | Production behavior verified against the installed package in the implementation phase, with a fallback that removes the execution target (research R9) |
| IX. Container-first | PASS | `dev` Compose service owns all dotnet work; agent container never runs `dotnet` |
| X. Docker reproducibility | PASS | Multi-stage `Dockerfile`, `Dockerfile.dev`, `compose.yaml`, `.dockerignore`, named volume |
| XI. Minimal testing | PASS | Two test projects, about 15 high-value tests, validation-gate coverage only |
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
Dockerfile                       # multi-stage: sdk:10.0 build/publish -> aspnet:10.0 runtime
Dockerfile.dev                   # sdk:10.0 + dotnet-ef tool
compose.yaml                     # services: dev, auth-api, migrate; volume: auth-data
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
│   └── Sessions/RefreshToken.cs             # rotation / revoke / reuse state machine
├── AuthService.Application/
│   ├── Abstractions/                        # only ports used by more than one slice
│   │   ├── IAccessTokenIssuer.cs
│   │   ├── IClock.cs                        # only if TimeProvider is not sufficient
│   │   └── IUserAccounts.cs                 # find/create/enable/roles/password over Identity
│   └── Features/
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
│   ├── Identity/                            # ApplicationUser, IUserAccounts adapter, role seeding
│   ├── Tokens/                              # signing key loader, JWT issuer, JWKS builder
│   ├── Sessions/                            # refresh-token store (hashing)
│   ├── Bootstrap/                           # first-Administrator hosted service
│   └── DependencyInjection.cs
└── AuthService.Api/
    ├── Program.cs                           # composition root
    ├── Features/<Feature>Endpoints.cs       # route groups, DTO binding, TypedResults
    ├── Security/                            # policies, rate limiting, CORS, options validation
    ├── OpenApi/                             # transformers, Scalar setup
    └── Properties/

tests/
├── AuthService.IntegrationTests/            # WebApplicationFactory + real SQLite + generated test key
└── AuthService.ArchitectureTests/           # four reference/boundary checks
```

**Structure Decision**: four `src` projects matching the constitution's boundaries, plus two test
projects. Ports in `Application/Abstractions` are limited to `IUserAccounts` (wraps Identity
`UserManager`/`RoleManager`, shared by several slices), `IAccessTokenIssuer` (token signing), and a
refresh-token store port used by the refresh, logout, login and disable slices. A slice-private
abstraction lives in its own slice folder. No generic repository or unit of work.

## Key Design Decisions (summary; detail in research.md)

- **Signing**: RS256 with an RSA key supplied by configuration, either a PEM private key file or a
  PFX certificate. `kid` is the key's thumbprint. Previous public keys are listed in configuration for
  rotation overlap and published, with the current one, at `GET /.well-known/jwks.json`.
- **Refresh tokens**: 32 random bytes, base64url; SHA-256 hash stored. A login creates a token family
  (session). Rotation marks the old token `Rotated` and issues a new one in the same family; reuse of a
  `Rotated` or `Revoked` token revokes the family. Logout revokes the presented token's family only.
  Disable and password reset revoke all of the user's families.
- **Bootstrap**: hosted service at startup. If `Users` is empty and `Bootstrap__AdminEmail` /
  `Bootstrap__AdminPassword` are set, create one Administrator. If the store is non-empty it does
  nothing and logs an informational line. Empty store with no bootstrap configuration in Production
  fails startup with a clear message.
- **Password reset**: Identity password-reset token (data-protection based, 24 h lifespan, invalidated
  by the password change through the security stamp). Admin endpoint returns it once; user redeems it at
  a public endpoint. Redemption revokes all sessions.
- **Password rule**: Identity `PasswordOptions` (8 chars, upper, lower, digit); no lockout-based
  account-existence leak, because lockout failures return the same generic 401.
- **Migrations**: explicit. The runtime image supports `--migrate` (apply and exit); compose provides a
  `migrate` service. The `dev` service sets `Database__MigrateOnStartup=true` for convenience only.
- **Endpoints added beyond the suggested list, required by clarified requirements**:
  `POST /api/users/{id}/password-reset`, `POST /api/auth/reset-password`,
  `GET /.well-known/jwks.json`.

## Complexity Tracking

No constitution violations; nothing to justify.
