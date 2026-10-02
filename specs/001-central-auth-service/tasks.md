---

description: "Task list for the Central Authentication and Authorization Service"
---

# Tasks: Central Authentication and Authorization Service

**Input**: Design documents from `/specs/001-central-auth-service/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present)

**Tests**: Requested by the plan and kept deliberately minimal (Phase 7 only). No coverage target.

**Organization**: Phases follow the requested implementation progression. Each phase from 3 onward
delivers one or more user stories so it can be validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 sign-in and token use, US2 role-based authorization, US3 user administration,
  US4 session renewal and logout, US5 current identity, US6 first Administrator, US7 consumer
  integration and operations, US8 API reference documentation

## Execution Rules (apply to every task)

- **Docker only.** The agent container has no .NET. Every `dotnet` command runs as
  `docker compose run --rm dev dotnet ...` (or `docker compose exec dev ...`). Never install the SDK,
  EF tools or SQLite tooling in the agent container. Never infer build or test status; run the command
  and read its output.
- **Host-path caveat (checklist CHK060).** Bind-mount sources are resolved by the host Docker daemon.
  When the agent itself runs in a container, compose must receive the host path of the repository via
  `HOST_PROJECT_DIR` (see T003). Do not use paths that exist only inside the agent container.
- **Paths** are relative to the repository root. Namespaces follow project names.
- **Scope guard.** No MediatR, generic repository, custom unit of work, event bus, OAuth/OIDC server,
  social login, MFA, or role CRUD. Role names always come from `AppRoles`, never string literals.
- Never log passwords, access tokens, refresh tokens, reset codes, private keys, certificate passwords.

---

## Phase 1: Solution and Docker Development Environment

**Purpose**: Reproducible Docker-only toolchain and the four-layer solution skeleton.

- [ ] T001 [P] Create `.dockerignore` excluding `bin/`, `obj/`, `.git/`, `.env`, `secrets/`, `*.pfx`, `*.pem`, `*.db`, `.specify/`, `specs/`, `.claude/`
- [ ] T002 [P] Create `Dockerfile.dev` from `mcr.microsoft.com/dotnet/sdk:10.0` that installs the `dotnet-ef` global tool (version matching EF Core 10), sets `DOTNET_CLI_HOME`, `NUGET_PACKAGES=/nuget`, `DOTNET_NOLOGO=1`, and `WORKDIR /workspace`; no application source is copied (source is bind-mounted)
- [ ] T003 Create `compose.yaml` with the `dev` service: build `Dockerfile.dev`, bind-mount `${HOST_PROJECT_DIR:-.}:/workspace`, run as `${LOCAL_UID:-1000}:${LOCAL_GID:-1000}` so generated files are not root-owned, named volume `nuget-cache` at `/nuget`, named volume `auth-data` at `/data`, `ASPNETCORE_ENVIRONMENT=Development`, `Database__MigrateOnStartup=true`, publish port `8080:8080` for `dotnet run` workflows; `tty`/`stdin_open` off (depends on T002)
- [ ] T004 [P] Create `.env.example` documenting `HOST_PROJECT_DIR`, `LOCAL_UID`, `LOCAL_GID`, `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword`, `Jwt__*` placeholders; contains no real secrets; add `.env` and `secrets/` to `.gitignore`
- [ ] T005 **Validate**: run `docker compose build dev` then `docker compose run --rm dev dotnet --info`; confirm SDK major version is 10 and `docker compose run --rm dev dotnet ef --version` prints a version (depends on T003)
- [ ] T006 [P] Create `global.json` pinning the .NET 10 SDK with `rollForward: latestFeature`, `Directory.Build.props` (`Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `AnalysisLevel=latest-recommended`, `TreatWarningsAsErrors=false` but `WarningsAsErrors` for security analyzer rules CA2100, CA3075, CA5350, CA5351, CA5379, CA5400, CA5401), and `Directory.Packages.props` with central package management enabled
- [ ] T007 Create the solution and projects through Docker: `AuthService.slnx`; classlib `src/AuthService.Domain`, `src/AuthService.Application`, `src/AuthService.Infrastructure`; web project `src/AuthService.Api` (minimal API template, no controllers); xunit projects `tests/AuthService.IntegrationTests` and `tests/AuthService.ArchitectureTests`; add all to the solution (depends on T005, T006). Do NOT create `AuthService.Application.Tests` (Plan §Constitution Check)
- [ ] T008 Establish architectural project references only: Application → Domain; Infrastructure → Application, Domain; Api → Application, Infrastructure; IntegrationTests → Api, Infrastructure; ArchitectureTests → Domain, Application, Infrastructure, Api. Domain references nothing (depends on T007)
- [ ] T009 Add package references (versions in `Directory.Packages.props`, latest .NET 10 compatible): Infrastructure gets `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.IdentityModel.JsonWebTokens`; Api gets `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Microsoft.EntityFrameworkCore.Design`; Application gets only `Microsoft.Extensions.Logging.Abstractions` if needed; Domain gets nothing; IntegrationTests gets `Microsoft.AspNetCore.Mvc.Testing`; ArchitectureTests gets `NetArchTest.Rules` (depends on T008)
- [ ] T010 **Validate Phase 1**: `docker compose run --rm dev dotnet restore`, `dotnet build --no-restore`; both succeed with zero errors; remove template placeholder files (`WeatherForecast`, `Class1.cs`, sample tests) (depends on T009)

**Checkpoint**: A Docker-only workflow restores and builds an empty four-layer solution.

---

## Phase 2: Persistence and Identity Foundation (Blocking)

**Purpose**: Database, Identity, fixed roles, migrations, bootstrap foundation. Blocks all later phases.

- [ ] T011 [P] [US2] Create `src/AuthService.Domain/Roles/AppRoles.cs`: canonical constants `Administrator`, `Manager`, `Operator`, `All` read-only list, and `TryParse(string?, out string canonicalName)` using exact (ordinal) matching; no framework references
- [ ] T012 [P] Create `src/AuthService.Infrastructure/Persistence/DatabaseOptions.cs` (`Path` default `/data/auth.db`, `MigrateOnStartup` default `false`) validated on start; bind from section `Database`
- [ ] T013 Create `src/AuthService.Infrastructure/Identity/ApplicationUser.cs : IdentityUser<Guid>` adding `IsEnabled` (bool, default `true`) and `CreatedAt` (UTC `DateTimeOffset`); email stays the only user-facing identifier and `UserName` is set equal to email internally (data-model User)
- [ ] T014 Create `src/AuthService.Infrastructure/Persistence/AuthDbContext.cs : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` with SQLite-compatible configuration and a unique index on `NormalizedEmail` (data-model Indices) (depends on T013)
- [ ] T015 Create `src/AuthService.Infrastructure/Persistence/AuthDbContextFactory.cs` (design-time `IDesignTimeDbContextFactory`) using a development database path so `dotnet ef` works in the dev container (depends on T014)
- [ ] T016 Create `src/AuthService.Infrastructure/DependencyInjection.cs` with `AddInfrastructure(IConfiguration)` registering `AuthDbContext` (SQLite from `DatabaseOptions.Path`), Identity (`RequireUniqueEmail=true`; `PasswordOptions` RequiredLength 8, RequireUppercase, RequireLowercase, RequireDigit, RequireNonAlphanumeric false; Lockout MaxFailedAccessAttempts 5, DefaultLockoutTimeSpan 15 minutes, AllowedForNewUsers true; `DataProtectionTokenProviderOptions.TokenLifespan` 24 hours), `AddDataProtection().PersistKeysToFileSystem("/data/keys")` path from configuration, `TimeProvider.System`; no `EnsureCreated` anywhere (depends on T012, T014)
- [ ] T017 Create `src/AuthService.Infrastructure/Identity/RoleSeeder.cs`: idempotently ensure exactly the roles in `AppRoles.All` exist (create missing only, never delete or rename); expose `SeedAsync` (depends on T011, T016)
- [ ] T018 Create `src/AuthService.Infrastructure/Bootstrap/BootstrapOptions.cs` (`AdminEmail`, `AdminPassword`) and `BootstrapAdministratorService.cs` (hosted service): after migration check and role seeding, if the Users table has zero rows AND both options are set, create one user with the Administrator role and `IsEnabled=true` inside a single transaction (user and role assignment succeed or fail together, CHK049); if any user exists do nothing and log `Bootstrap skipped: users already exist`; if the store is empty and options are missing, log a clear error and fail startup in Production; never log the password (depends on T017)
- [ ] T019 Wire `src/AuthService.Api/Program.cs` as composition root: call `AddInfrastructure`, apply `--migrate` switch (when `args` contains `--migrate`, apply pending EF migrations then exit with code 0, nonzero on failure), apply migrations on startup only when `Database:MigrateOnStartup` is true, run `RoleSeeder` before the host starts serving, register the bootstrap hosted service; no endpoints yet (depends on T016, T017, T018)
- [ ] T020 Create the initial migration through Docker: `docker compose run --rm dev dotnet ef migrations add InitialIdentity --project src/AuthService.Infrastructure --startup-project src/AuthService.Api --output-dir Persistence/Migrations`; commit the generated files (depends on T015, T019)
- [ ] T021 **Validate Phase 2**: `docker compose run --rm dev dotnet build`; run `docker compose run --rm -e Bootstrap__AdminEmail=admin@example.test -e 'Bootstrap__AdminPassword=Str0ngPassw0rd' dev dotnet run --project src/AuthService.Api -- --migrate`; open the SQLite file in `/data` read-only through a throwaway container (e.g. `docker run --rm -v <volume>:/data keinos/sqlite3 sqlite3 /data/auth.db 'select Name from AspNetRoles'`) and confirm exactly three roles; repeat the run and confirm roles are not duplicated (depends on T020)

**Checkpoint**: A migrated SQLite database exists on the volume with seeded roles and a working bootstrap
foundation.

---

## Phase 3: Authentication and JWT (US1, US2 claims, US6 completion)

**Goal**: A valid enabled user signs in with email and password and receives an asymmetrically signed
access token that consumers can verify with public material only.

**Independent Test**: Sign in as the bootstrap Administrator, decode the token, confirm `sub`, `role`,
`iss`, `aud`, `exp` and `kid`, fetch the JWKS, and confirm wrong password, unknown email and disabled
accounts return an identical 401.

### Implementation

- [ ] T022 [P] [US1] Create `src/AuthService.Application/Abstractions/IAccessTokenIssuer.cs` (`Issue(userId, roles)` returning token string and expiry) and `src/AuthService.Application/Abstractions/IUserAccounts.cs` with the login-time operation `VerifyCredentialsAsync(email, password)` returning a result of `Succeeded(userId, email, roles)` or `Failed` (single failure outcome covering unknown email, wrong password, disabled, lockout)
- [ ] T023 [P] [US1] Create `src/AuthService.Infrastructure/Tokens/JwtOptions.cs`: `Issuer`, `Audience`, `AccessTokenMinutes` default 15, `Signing:PrivateKeyPemPath`, `Signing:CertificatePath`, `Signing:CertificatePassword`, `PreviousPublicKeyPaths`; validator requires issuer and audience, exactly one signing source, RSA key of at least 2048 bits, `AccessTokenMinutes` between 1 and 60; `ValidateOnStart` (Plan §R1, §R11)
- [ ] T024 [US1] Create `src/AuthService.Infrastructure/Tokens/SigningKeyProvider.cs`: load the current RSA private key from PEM or PFX (never exposing it outside the class), compute `kid` as base64url SHA-256 of the public key, load previous public keys (PEM or certificate public part), and expose `SigningCredentials` (RS256) plus the list of public `JsonWebKey`s. In Development only, when no key is configured, generate a throwaway key under `/data/dev-keys`; in any other environment fail startup with a clear message (depends on T023)
- [ ] T025 [US1] Create `src/AuthService.Infrastructure/Tokens/JwtAccessTokenIssuer.cs` implementing `IAccessTokenIssuer`: claims `sub`, one `role` per role, `iss`, `aud`, `iat`, `nbf`, `exp`, `jti`; header `kid`; no email or profile data (Plan §R3); use `TimeProvider` (depends on T022, T024)
- [ ] T026 [US1] Create `src/AuthService.Infrastructure/Identity/UserAccounts.cs` implementing `VerifyCredentialsAsync` with `UserManager<ApplicationUser>`: find by email, if not found verify the password against a fixed dummy hash to keep timing close, reject disabled users and locked-out users with the same `Failed` result, count failed attempts through Identity lockout, return roles on success (depends on T022, T016)
- [ ] T027 [P] [US1] Create the Login slice in `src/AuthService.Application/Features/Authentication/Login/`: `LoginRequest(Email, Password)`, `LoginResponse(AccessToken, ExpiresInSeconds)` (refresh fields added in T038), `LoginHandler` using `IUserAccounts` and `IAccessTokenIssuer`, returning a result type with a single generic failure; validation rejects empty/malformed email and empty password with a validation outcome that never mentions account existence (depends on T022)
- [ ] T028 [US1] Create `src/AuthService.Api/Security/JwtBearerSetup.cs` configuring JWT Bearer with explicit validation of issuer, audience, signature (public keys from `SigningKeyProvider`, matched by `kid`) and lifetime (clock skew 1 minute), `MapInboundClaims=false`, `NameClaimType="sub"`, `RoleClaimType="role"` (depends on T024)
- [ ] T029 [US2] Create `src/AuthService.Api/Security/AuthorizationPolicies.cs` defining policy `RequireAdministrator` using `AppRoles.Administrator`; no string literal role names elsewhere (depends on T011)
- [ ] T030 [US1] Create `src/AuthService.Api/Features/Authentication/AuthEndpoints.cs` with `MapAuthEndpoints` route group `/api/auth`: `POST /login` using `TypedResults`, 200 with `LoginResponse`, 401 Problem Details titled `Invalid credentials` for the generic failure, 400 for validation (depends on T027)
- [ ] T031 [P] [US1] Create `src/AuthService.Infrastructure/Tokens/JwksBuilder.cs` producing the JWKS document (public RSA parameters only, `kid`, `alg=RS256`, `use=sig`) and `src/AuthService.Api/Features/Metadata/JwksEndpoint.cs` mapping anonymous `GET /.well-known/jwks.json` with `Cache-Control: public, max-age=300` (depends on T024)
- [ ] T032 [US1] Register Application handlers, Infrastructure services, authentication, authorization and endpoints in `src/AuthService.Api/Program.cs` (add `UseAuthentication`/`UseAuthorization` before endpoints) (depends on T028, T029, T030, T031)
- [ ] T033 **Validate Phase 3**: build through Docker; start with `docker compose run --rm --service-ports dev dotnet run --project src/AuthService.Api` using the bootstrap variables; from the agent shell, `curl` login with correct password (200), wrong password, unknown email and a manually disabled account (identical 401 bodies); base64-decode the token header and payload to confirm `kid`, `sub`, `role`, `iss`, `aud`, `exp`; confirm `/.well-known/jwks.json` contains no `d`, `p` or `q` fields (depends on T032)

**Checkpoint**: Sign-in works end to end with asymmetric tokens and a published public key.

---

## Phase 4: Session Renewal (US4)

**Goal**: Rotating, revocable, hashed refresh tokens with reuse detection and per-session logout.

**Independent Test**: Sign in, renew once, reuse the first refresh token (rejected and session revoked),
sign in again, log out, and fail to renew.

### Implementation

- [ ] T034 [P] [US4] Create `src/AuthService.Domain/Sessions/RefreshToken.cs`: fields `Id`, `FamilyId`, `UserId`, `TokenHash`, `CreatedAt`, `ExpiresAt`, `State` (`Active`, `Rotated`, `Revoked`), `ReplacedByTokenId`, `RevokedAt`, `RevokedReason` (`Logout`, `ReuseDetected`, `UserDisabled`, `PasswordReset`); methods `Rotate(now, replacementId)` allowed only when `Active` and unexpired, `Revoke(reason, now)`, and `IsUsable(now)`; no framework references (data-model RefreshToken)
- [ ] T035 [P] [US4] Create `src/AuthService.Application/Abstractions/IRefreshTokenStore.cs` with only the operations the slices need: create session, find by token hash, rotate (atomic: mark old `Rotated`, insert new in same family), revoke family, revoke all for user
- [ ] T036 [P] [US4] Create `src/AuthService.Infrastructure/Sessions/SessionOptions.cs` (`RefreshTokenDays` default 7, validated 1 to 90) and `RefreshTokenGenerator.cs` producing 32 random bytes base64url and a SHA-256 hash helper (hash stored, raw value never stored)
- [ ] T037 [US4] Create `src/AuthService.Infrastructure/Sessions/RefreshTokenStore.cs` implementing `IRefreshTokenStore` on `AuthDbContext` with a transaction for rotation; add `DbSet<RefreshToken>` and entity configuration in `src/AuthService.Infrastructure/Persistence/RefreshTokenConfiguration.cs` with unique index on `TokenHash`, indices on `FamilyId` and `UserId`, FK to users (depends on T034, T035, T036)
- [ ] T038 [US4] Extend the Login slice: `LoginResponse` gains `RefreshToken` and `RefreshTokenExpiresAt`; `LoginHandler` creates a new session family and returns the raw refresh token once (depends on T027, T037)
- [ ] T039 [P] [US4] Create `src/AuthService.Application/Features/Authentication/RefreshToken/` (`RefreshTokenRequest`, `RefreshTokenHandler`): look up by hash; unknown, expired or disabled-user → generic unauthorized; `Rotated` or `Revoked` token → revoke the whole family with `ReuseDetected` and return unauthorized; success → rotate and issue new access token plus new refresh token (depends on T035, T022)
- [ ] T040 [P] [US4] Create `src/AuthService.Application/Features/Authentication/Logout/` (`LogoutRequest`, `LogoutHandler`): revoke only the presented token's family with reason `Logout`; unknown or already revoked token still succeeds (idempotent 204) (depends on T035)
- [ ] T041 [US4] Add `POST /api/auth/refresh` and `POST /api/auth/logout` to `src/AuthService.Api/Features/Authentication/AuthEndpoints.cs` per contracts/http-api.md (200/401 and 204) (depends on T038, T039, T040)
- [ ] T042 [US4] Add migration `AddRefreshTokens` through Docker (`dotnet ef migrations add AddRefreshTokens ...`) and apply it with `docker compose run --rm dev dotnet run --project src/AuthService.Api -- --migrate` (depends on T037)
- [ ] T043 **Validate Phase 4**: with `curl`, sign in, refresh (new tokens differ), reuse the first refresh token (401), confirm the latest token of that session is now also rejected, sign in again, call logout, then refresh (401); confirm a second session of the same user was unaffected; inspect the database with the throwaway sqlite container and confirm `TokenHash` values are hashes and no raw token appears (depends on T041, T042)

**Checkpoint**: Session renewal and revocation behave exactly as clarified.

---

## Phase 5: Current Identity and User Administration (US5, US3, US2)

**Goal**: Authenticated users see their identity; Administrators manage users, roles and password
resets; Managers and Operators are refused.

**Independent Test**: As Administrator create a user, assign and remove roles, disable and enable,
issue and redeem a reset code; as Manager and Operator confirm 403 on every administration route.

### Implementation

- [ ] T044 [US3] Extend `src/AuthService.Application/Abstractions/IUserAccounts.cs` with: `GetByIdAsync`, `ListAsync(skip, take)`, `CreateAsync(email, password, roles)`, `SetEnabledAsync`, `AddToRoleAsync`, `RemoveFromRoleAsync`, `CountEnabledAdministratorsAsync`, `GeneratePasswordResetCodeAsync`, `ResetPasswordAsync(email, code, newPassword)`; result types distinguish `NotFound`, `Conflict`, `ValidationFailed` (with reasons, never including the password) and `Succeeded`
- [ ] T045 [US3] Implement the new members in `src/AuthService.Infrastructure/Identity/UserAccounts.cs`: creation sets `IsEnabled=true` and `CreatedAt`, duplicate email → `Conflict`, password rule violations → `ValidationFailed`; reset code via `GeneratePasswordResetTokenAsync`, redemption via `ResetPasswordAsync` (security stamp change makes the code single-use); disabling sets `IsEnabled=false` (depends on T044)
- [ ] T046 [P] [US5] Create `src/AuthService.Application/Features/Identity/GetCurrentUser/` (`CurrentUserResponse(Id, Email, Roles)`, handler reading the user id from the caller identity abstraction passed in by the endpoint; unknown or disabled user → unauthorized) (depends on T044)
- [ ] T047 [P] [US3] Create `src/AuthService.Application/Features/Users/ListUsers/` (`skip` default 0, `take` default 50 and maximum 200, `UserDto(Id, Email, IsEnabled, Roles, CreatedAt)`) and `GetUser/` (404 when absent) (depends on T044)
- [ ] T048 [US3] Create `src/AuthService.Application/Features/Users/CreateUser/` (`CreateUserRequest(Email, Password, Roles)`): require at least one role; every role must pass `AppRoles.TryParse` else validation failure; duplicate email → conflict; response is `UserDto` (depends on T044, T011)
- [ ] T049 [P] [US3] Create `src/AuthService.Application/Features/Users/EnableUser/` and `DisableUser/`: disabling refuses (conflict) when the user is the last enabled Administrator; disabling calls `IRefreshTokenStore` to revoke all of that user's sessions with reason `UserDisabled` (depends on T044, T035)
- [ ] T050 [P] [US2] Create `src/AuthService.Application/Features/Roles/AssignRole/` and `RemoveRole/`: the role value must pass `AppRoles.TryParse` else 400; assignment is idempotent; removal refuses when it is the user's last role or would leave zero enabled Administrators (409); roles are never created (depends on T044, T011)
- [ ] T051 [P] [US3] Create `src/AuthService.Application/Features/Users/IssuePasswordReset/`: Administrator-only; returns the reset code once with `ExpiresAt` (creation time plus 24 hours); unknown user → 404 (depends on T044)
- [ ] T052 [P] [US3] Create `src/AuthService.Application/Features/Authentication/ResetPassword/` (`ResetPasswordRequest(Email, ResetCode, NewPassword)`): invalid, expired, used code or unknown email → one identical unauthorized outcome; weak password → validation failure; success revokes all of the user's sessions with reason `PasswordReset` (depends on T044, T035)
- [ ] T053 [US5] Create `src/AuthService.Api/Features/Identity/MeEndpoints.cs` mapping `GET /api/me` with `RequireAuthorization()`; read `sub` from the principal (depends on T046)
- [ ] T054 [US3] Create `src/AuthService.Api/Features/Users/UserEndpoints.cs` mapping a route group `/api/users` with `RequireAuthorization(AuthorizationPolicies.RequireAdministrator)` containing: `GET /`, `GET /{id}`, `POST /` (201 with `Location`), `POST /{id}/enable`, `POST /{id}/disable`, `POST /{id}/password-reset`, `PUT /{id}/roles/{role}`, `DELETE /{id}/roles/{role}`, using explicit DTOs and `TypedResults` per contracts/http-api.md (depends on T047, T048, T049, T050, T051)
- [ ] T055 [US3] Add `POST /api/auth/reset-password` (anonymous, 204/400/401) to `src/AuthService.Api/Features/Authentication/AuthEndpoints.cs` and register all new endpoint groups and handlers in `src/AuthService.Api/Program.cs` (depends on T052, T053, T054)
- [ ] T056 **Validate Phase 5**: build through Docker; run the service and use `curl` to cover: `/api/me` with and without token; Administrator creates Manager and Operator accounts; Manager and Operator get 403 on `GET /api/users`; `PUT /api/users/{id}/roles/Root` returns 400; removing a user's only role returns 409; disabling the only Administrator returns 409; issue reset code, redeem it, old sessions fail, new password signs in; weak new password returns 400 (depends on T055)

**Checkpoint**: All functional endpoints exist and authorization is enforced by policy.

---

## Phase 6: OpenAPI, Scalar and Runtime Hardening (US8 and cross-cutting)

**Purpose**: Documentation modes, error handling, configuration safety, CORS, throttling, health, and the
production container.

- [ ] T057 [P] [US8] Create `src/AuthService.Api/OpenApi/OpenApiSetup.cs`: `AddOpenApi` with a document transformer adding the bearer security scheme, titles and descriptions, and operation transformers marking anonymous versus authorized routes; document request/response DTOs (never Identity entities); map `/openapi/v1.json`
- [ ] T058 [US8] **Verify Scalar**: inside Docker, locate the restored `Scalar.AspNetCore` package (`docker compose run --rm dev sh -c 'ls /nuget/scalar.aspnetcore/*/lib/*/'`), read its XML documentation or inspect `ScalarOptions` members, and record in `specs/001-central-auth-service/research.md` (R9) the exact option names available for hiding the test-request client, proxy and default HTTP client, and which fallback (1, 2 or 3) will be used (depends on T009)
- [ ] T059 [US8] Create `src/AuthService.Api/OpenApi/ScalarSetup.cs`: Development maps Scalar at `/scalar` with interactive requests enabled and bearer auth selectable; Production maps Scalar with the mechanism chosen in T058 so no request can be executed, `servers` removed from the production OpenAPI document if fallback 3 is used, no example tokens, secrets or internal URLs (depends on T057, T058)
- [ ] T060 [P] Create `src/AuthService.Api/Security/ProblemDetailsSetup.cs`: `AddProblemDetails`, `UseExceptionHandler` mapping unhandled exceptions to generic 500 Problem Details without stack traces or internals outside Development, `UseStatusCodePages`, consistent 401/403/404/409/429 bodies
- [ ] T061 [P] Create `src/AuthService.Api/Security/StartupValidation.cs` and wire `ValidateOnStart` for `JwtOptions`, `SessionOptions`, `DatabaseOptions`, `BootstrapOptions` (password rule applied to the bootstrap password), `CorsOptions`; in Production an invalid or absent signing configuration, or an empty store without bootstrap values, stops startup with a clear non-secret message
- [ ] T062 [P] Create `src/AuthService.Api/Security/CorsSetup.cs`: `Cors:AllowedOrigins` from configuration; empty list means no cross-origin access in Production; no wildcard origin with credentials; Development may add a documented localhost default
- [ ] T063 [P] Create `src/AuthService.Api/Security/RateLimitingSetup.cs`: fixed-window per remote IP policies for `POST /api/auth/login`, `/api/auth/refresh`, `/api/auth/reset-password` (limits from `RateLimit` options, defaults 10 requests per minute), rejection returns 429 Problem Details; apply with `RequireRateLimiting`
- [ ] T064 [P] Create `src/AuthService.Api/Features/Metadata/HealthEndpoint.cs`: anonymous `GET /health` runs a database connectivity check and returns 200 `Healthy` or 503 `Unhealthy` with no further detail
- [ ] T065 [P] Add security-event logging without secrets: structured log entries for failed sign-in (no email, no password), reuse-detected session revocation, role assignment/removal and user enable/disable (actor id and target id only), bootstrap outcome; place them in Infrastructure adapters or Api endpoints; add a review note that no access token, refresh token, reset code or key is ever passed to a logger (CHK072)
- [ ] T066 Wire T057, T059 to T065 into `src/AuthService.Api/Program.cs` in the correct middleware order (exception handler, forwarded headers only when configured, CORS, rate limiter, authentication, authorization, endpoints) (depends on T057, T059, T060, T061, T062, T063, T064, T065)
- [ ] T067 Add a `--healthcheck` switch to `src/AuthService.Api/Program.cs` that performs an HTTP GET to `http://localhost:8080/health` and exits 0 or 1, so the runtime image needs no curl (depends on T064)
- [ ] T068 Create the production `Dockerfile`: stage 1 `mcr.microsoft.com/dotnet/sdk:10.0` restores and publishes `src/AuthService.Api` with `--no-self-contained`; stage 2 `mcr.microsoft.com/dotnet/aspnet:10.0` copies the publish output only, runs as a non-root user, sets `ASPNETCORE_URLS=http://+:8080`, declares `VOLUME /data`, `HEALTHCHECK CMD ["dotnet","AuthService.Api.dll","--healthcheck"]`; no private keys, `.env` or databases are copied (depends on T067)
- [ ] T069 Extend `compose.yaml` with `auth-api` (build `Dockerfile`, port `8080:8080`, `auth-data:/data`, key material via read-only bind mount of `${SIGNING_KEY_DIR:-./secrets}` to `/run/secrets/signing`, environment from `.env`, `ASPNETCORE_ENVIRONMENT=Production` by default) and a one-shot `migrate` service using the same image with command `--migrate` and the same volume (depends on T068)
- [ ] T070 **Validate Phase 6**: `docker compose build`; start `auth-api` in Production without a signing key and confirm it exits with a clear message; generate a throwaway RSA key into `secrets/` using `docker run --rm -v "$PWD/secrets:/out" alpine/openssl genrsa ...` (not installed in the agent), run `docker compose run --rm migrate`, start `auth-api`, check `/health`, call `/openapi/v1.json`, open `/scalar` and confirm no executable request target exists; run `docker compose down` then `up` and confirm the user created earlier still signs in; confirm Development mode `/scalar` is interactive (depends on T066, T069)

**Checkpoint**: The service is hardened and runnable from the production image with persistent storage.

---

## Phase 7: Minimal Verification

**Purpose**: The smallest suite that proves the validation gate. Do not add tests beyond this list.

- [ ] T071 Create the integration fixture `tests/AuthService.IntegrationTests/AuthServiceFactory.cs`: `WebApplicationFactory<Program>` using a temp-file SQLite database, migrations applied, a generated RSA PEM key written to a temp path, fixed issuer and audience, and helper methods that create an enabled user per role (Administrator, Manager, Operator) and a disabled user through the real Identity stack (depends on T066)
- [ ] T072 [P] [US1] `tests/AuthService.IntegrationTests/LoginTests.cs`: valid credentials return a token; wrong password and unknown email return the same status and body; disabled user cannot sign in (depends on T071)
- [ ] T073 [P] [US1] [US2] `tests/AuthService.IntegrationTests/JwtTests.cs`: validate an issued token with an independent `JwtBearer`-style validation configured only with the public key from the JWKS endpoint; wrong audience and wrong issuer are rejected; expected `role` claims present (depends on T071)
- [ ] T074 [P] [US2] [US3] `tests/AuthService.IntegrationTests/AuthorizationTests.cs`: Administrator succeeds on `GET /api/users`; Manager and Operator receive 403; unauthenticated receives 401 (depends on T071)
- [ ] T075 [P] [US2] `tests/AuthService.IntegrationTests/RoleAssignmentTests.cs`: assigning a supported role succeeds; an unsupported role is rejected with 400 and nothing changes; removing the last role is refused (depends on T071)
- [ ] T076 [P] [US4] `tests/AuthService.IntegrationTests/RefreshTokenTests.cs`: renewal returns new tokens; reusing the rotated token fails and revokes the session; logout revokes the session (depends on T071)
- [ ] T077 [P] [US6] `tests/AuthService.IntegrationTests/BootstrapTests.cs`: bootstrap creates the Administrator only when the store is empty and does nothing when a user already exists (depends on T071)
- [ ] T078 [P] [US3] `tests/AuthService.IntegrationTests/PersistenceTests.cs`: create a user, dispose the host, start a new host on the same database file and confirm the user, roles and a refresh-token record are still present (real Identity + EF Core + SQLite) (depends on T071)
- [ ] T079 [P] `tests/AuthService.ArchitectureTests/DependencyRulesTests.cs` using `NetArchTest.Rules`: Domain has no reference to Infrastructure, ASP.NET Core, EF Core or Identity; Application has no reference to Infrastructure or Api (four checks, nothing more)
- [ ] T080 **Validate Phase 7**: `docker compose run --rm dev dotnet test`; all tests pass; confirm no coverage collection or threshold configuration exists anywhere in the repository (depends on T072 to T079)

**Checkpoint**: The minimum validation gate (spec FR-029, constitution XI) is automated and green in Docker.

---

## Phase 8: Documentation and Final Container Validation (US7, US8)

**Purpose**: Everything a new operator or developer needs, then a clean-room validation through Docker.

- [ ] T081 [P] [US7] Create `README.md`: purpose, quick start (Docker only), repository map, links to `docs/`
- [ ] T082 [P] [US7] Create `docs/architecture.md`: layers, allowed dependency directions, vertical slice layout, where business rules live, diagram of request flow
- [ ] T083 [P] [US7] Create `docs/development.md`: assumes Docker and no local .NET; start dev environment, restore, build, test, create and apply migrations, run the app, stop and remove containers and volumes; the `HOST_PROJECT_DIR` requirement when the developer or agent itself runs in a container
- [ ] T084 [P] [US7] Create `docs/authentication.md`: login, refresh, logout and reset flows with request/response examples, uniform failure behavior, lockout and rate limits, password rule, TLS expectation (terminate TLS in front of the service; tokens and credentials must not travel over plain HTTP)
- [ ] T085 [P] [US7] Create `docs/authorization.md`: fixed role set, policy usage, Administrator-only operations, last-Administrator and last-role guards, trust model including that an Administrator who issues a reset code can take over that account, acceptable lag for role and disable changes (token lifetime)
- [ ] T086 [P] [US7] Create `docs/jwt-consumer-integration.md`: an independent ASP.NET Core consumer configuring JWT Bearer with issuer, audience, `kid`-aware key resolution via JWKS or a static public key, `NameClaimType="sub"`, `RoleClaimType="role"`, `MapInboundClaims=false`; authorization policy examples for Administrator, Manager and Operator; rotation handling; clock skew; statement that refresh tokens never go to consumers
- [ ] T087 [P] [US7] Create `docs/configuration.md`: every setting (`Jwt__*`, `Session__*`, `Bootstrap__*`, `Database__*`, `Cors__*`, `RateLimit__*`), defaults, required in Production, which ones cause startup failure
- [ ] T088 [P] [US7] Create `docs/database.md`: SQLite location `/data/auth.db`, volume persistence, data-protection key ring at `/data/keys` and why it must be backed up, migrations (create, apply, failure handling and restore path), backup (stop the service, copy database and key ring, or SQLite online backup) and restore procedure with verification, single-instance limitation
- [ ] T089 [P] [US7] Create `docs/deployment.md`: production container deployment, required external configuration, private signing material handling (mounted secret, file permissions, never in image or repository), public key distribution to consumers, certificate provisioning and creation of a development certificate, expiration monitoring and what happens when a certificate expires, rotation overview, Scalar/OpenAPI production behavior, TLS termination, reverse proxy forwarded headers
- [ ] T090 [P] [US7] Create `docs/operations.md`: health checks, logs and which security events appear, first Administrator bootstrap (how configured, when permitted, how initialization is detected, how to confirm it is closed, advice to remove the bootstrap password), migrations, key/certificate rotation step by step including emergency replacement, backup and recovery, common authentication failures (401 causes, 403 causes, expired token, wrong audience, unknown `kid`, clock skew, locked-out user, rate limited)
- [ ] T091 **Final validation (clean state)**: remove local build output and volumes (`docker compose down -v`), then run `docker compose build` and confirm success (depends on T068, T069, T081 to T090)
- [ ] T092 **Final validation (restore/build/test)**: run `docker compose run --rm dev dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build` and confirm all succeed; paste actual output summary into the task notes (depends on T091)
- [ ] T093 **Final validation (migrations)**: create a throwaway migration with `docker compose run --rm dev dotnet ef migrations add ValidationProbe ...`, confirm it is generated, remove it with `dotnet ef migrations remove`, then apply the real migrations with `docker compose run --rm migrate` (depends on T092)
- [ ] T094 **Final validation (startup and smoke)**: start `auth-api` with bootstrap values and a generated key; execute every step of `specs/001-central-auth-service/quickstart.md` (login, `/api/me`, role denial, refresh rotation and reuse, logout, bootstrap closed on restart, persistence across `docker compose down` and `up`, JWKS validation, Development versus Production Scalar behavior); record real results (depends on T093)
- [ ] T095 **Final validation (Docker-only compliance)**: confirm `which dotnet` fails in the agent container, that no task in this file invokes `dotnet` outside Docker, and update `specs/001-central-auth-service/checklists/security.md` notes with any item that is now resolved (reviewer still owns the checkboxes) (depends on T094)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1** (Setup): no dependencies; T001, T002, T004, T006 run in parallel, then T003, T005, T007 to T010 sequentially
- **Phase 2** (Foundation): depends on Phase 1; blocks all later phases
- **Phase 3** (US1/US2 claims/US6): depends on Phase 2
- **Phase 4** (US4): depends on Phase 3 (login and token issuer)
- **Phase 5** (US5/US3/US2): depends on Phase 4 (disable and reset revoke sessions) and Phase 3 (policy)
- **Phase 6** (hardening): depends on Phase 5 for complete endpoints; T057/T058 may start after Phase 3
- **Phase 7** (tests): depends on Phase 6 wiring (T066); test files run in parallel
- **Phase 8** (docs and validation): docs T081 to T090 may start once Phase 5 is complete; final validation depends on everything

### User Story Dependencies

- **US1** (sign-in): foundation only
- **US2** (roles): `AppRoles` in Phase 2, policy in Phase 3, role endpoints in Phase 5
- **US6** (bootstrap): foundation (T018), exercised from Phase 3 on
- **US4** (renewal): after US1
- **US5, US3**: after US1 and US4
- **US7, US8**: after the behavior they document

### Within Each Phase

- Domain and port definitions before adapters, adapters before handlers' endpoints, endpoints before wiring, wiring before the validation task
- Tasks editing the same file (for example `IUserAccounts.cs`, `UserAccounts.cs`, `AuthEndpoints.cs`, `Program.cs`) are never marked [P]

### Parallel Opportunities

- Phase 1: T001, T002, T004, T006
- Phase 3: T022, T023, T027 (after T022), T031
- Phase 4: T034, T035, T036, then T039, T040
- Phase 5: T046, T047, T049, T050, T051, T052 after T044
- Phase 6: T057, T060, T061, T062, T063, T064, T065
- Phase 7: T072 to T079 after T071
- Phase 8: T081 to T090

---

## Parallel Example: Phase 5

```bash
# After T044 (IUserAccounts extended), these touch different slice folders:
Task: "GetCurrentUser slice in src/AuthService.Application/Features/Identity/GetCurrentUser/"
Task: "ListUsers and GetUser slices in src/AuthService.Application/Features/Users/"
Task: "Enable/Disable slices in src/AuthService.Application/Features/Users/"
Task: "AssignRole/RemoveRole slices in src/AuthService.Application/Features/Roles/"
Task: "IssuePasswordReset and ResetPassword slices"
```

---

## Implementation Strategy

### MVP First (sign-in and token validation)

1. Phase 1 and Phase 2
2. Phase 3 (US1)
3. **Stop and validate** (T033): token issued, signature verifiable with the JWKS, generic failures
4. Run only T071 to T073 early as a smoke gate if desired

### Incremental Delivery

1. Add Phase 4 for sessions, validate with T043
2. Add Phase 5 for administration, validate with T056
3. Add Phase 6 hardening and the production image, validate with T070
4. Phase 7 tests, then Phase 8 documentation and the clean-room validation

### Notes

- Each validation task must be executed for real; failures are fixed before the next phase starts
- Commit after each task or logical group
- Do not add tests, endpoints or features beyond those listed
- Checklist `checklists/security.md` is reviewer-owned; implementation never ticks its boxes
