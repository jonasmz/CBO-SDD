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
  integration and operations, US8 API reference documentation. Foundation, hardening and final
  validation tasks carry no story label unless they deliver a specific story.

## Execution Rules (apply to every task)

- **Docker only.** The agent container has no .NET. Every `dotnet` command runs as
  `docker compose run --rm dev dotnet ...`. Never install the SDK, EF tools, sqlite3, openssl or curl in
  the agent container, and never run public images ad hoc with `docker run`: every helper comes from the
  repository-built `dev` or `tools` service. Never infer build or test status; run the command and read
  its output.
- **Agent in a container.** Bind-mount sources and published ports belong to the host, not to the agent.
  `HOST_PROJECT_DIR` must be the repository path as seen by the host Docker daemon (T005 fails fast
  without it). All HTTP checks and database inspections use the compose network through the `tools`
  service: `docker compose run --rm -T tools curl http://dev-api:8080/...` (Development) or
  `http://auth-api:8080/...` (production image); `docker compose run --rm -T -v authservice-dev-data:/data:ro
  tools sqlite3 /data/auth.db '...'`. Never use `localhost` published ports from the agent. Start
  services detached and wait with a polling loop (`until curl -sf <url>; do sleep 2; done`, bounded).
  Readiness before T069 exists polls `/.well-known/jwks.json`; afterwards `/health`.
- **Paths** are relative to the repository root. Namespaces follow project names.
- **Decisions vs mechanics.** Business decisions (disabled/locked users fail, bootstrap only on an empty
  store, duplicate email, password rule, last-Administrator and last-role guards, role validity) live in
  Domain or Application handlers. Infrastructure adapters return facts or perform mechanics (Identity
  calls, hashing, transactions). Ports are narrow and role-specific; do not create a catch-all service
  interface.
- **Scope guard.** No MediatR, generic repository, custom unit of work, event bus, OAuth/OIDC server,
  social login, MFA, or role CRUD. Role names always come from `AppRoles`, never string literals.
- Never log passwords, access tokens, refresh tokens, reset codes, private keys, certificate passwords.

---

## Phase 1: Solution and Docker Development Environment

**Purpose**: Reproducible Docker-only toolchain and the four-layer solution skeleton.

- [X] T001 [P] Create `.dockerignore` excluding `bin/`, `obj/`, `.git/`, `.env`, `secrets/`, `*.pfx`, `*.pem`, `*.p12`, `dev-keys/`, `*.db`, `.specify/`, `specs/`, `.claude/` (private-key-bearing files must never enter an image build context)
- [X] T002 [P] Create `Dockerfile.dev` from `mcr.microsoft.com/dotnet/sdk:10.0` that installs the `dotnet-ef` tool into a world-readable `--tool-path /opt/dotnet-tools` added to `PATH` (version matching EF Core 10; not a per-user global install, so the non-root `LOCAL_UID` can run it), sets `DOTNET_CLI_HOME=/tmp/dotnet`, `NUGET_PACKAGES=/nuget`, `DOTNET_NOLOGO=1`, `WORKDIR /workspace`, and creates `/data` and `/nuget` with mode `0777` so new named volumes are writable by any `LOCAL_UID`; no application source is copied
- [X] T003 [P] Create `Dockerfile.tools` from a pinned current stable `alpine` tag installing `sqlite`, `curl`, `openssl` and `bash`; default command `sh`; no volumes; this is the only place those utilities exist
- [X] T004 [P] Create `.env.example` documenting `HOST_PROJECT_DIR` (host path of the repository; required), `LOCAL_UID=1000` and `LOCAL_GID=1000` (identity of the development containers `dev`, `dev-api` and of the local validation helpers only; never used for the production runtime image), `API_PORT` (default 8080), `DEV_API_PORT` (default 8081), `SIGNING_KEY_DIR` (absolute host path; defaults to `${HOST_PROJECT_DIR}/secrets`, validation only), `APP_UID=10001` and `APP_GID=10001` (numeric runtime identity of the production image; the documented defaults and, together with deployment configuration, the one authoritative source when Compose is used; unrelated to the development-only `LOCAL_UID`/`LOCAL_GID`; `.env.example` comments MUST state that the two identity sets are independent, `LOCAL_*` applies only to development containers, `APP_*` only to the production runtime container, and they MUST NOT be mixed; `.env` is the normal authoritative Compose configuration, and any `:-1000` fallbacks in `compose.yaml` are only safe defaults), `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword`, `Jwt__*` placeholders; no real secrets; add `.env`, `secrets/`, `*.pem`, `*.pfx`, `*.p12` and `dev-keys/` to `.gitignore` (intentionally versioned public artifacts such as certificates or JWKS samples use other extensions like `.crt`, `.cer`, `.pub` or `.json`, or a documented `!` exception; private-key-bearing files stay protected); the `.env.example` header comments MUST say that a `.env` containing secrets must be mode 0600 where supported, stays out of version control, and that production should prefer mounted secret files or the platform's secret mechanism for the PFX password and the bootstrap Administrator password (environment variables can be visible through container inspection); no literal production secret may appear in `compose.yaml`
- [X] T005 Create `compose.yaml` with: service `dev` (build `Dockerfile.dev`, bind mount `${HOST_PROJECT_DIR:?set HOST_PROJECT_DIR to the repository path as seen by the host Docker daemon}:/workspace`, `user: "${LOCAL_UID:-1000}:${LOCAL_GID:-1000}"` (development identity; the `:-1000` fallbacks are only safe defaults, `.env` is authoritative), volumes `nuget-cache:/nuget` and `authservice-dev-data:/data`, `ASPNETCORE_ENVIRONMENT=Development`, `Database__MigrateOnStartup=true`, no `env_file`, so ambient `Bootstrap__*`/`Jwt__*` values never leak into `dotnet test`); service `dev-api` (same image and mounts, `env_file: .env` so `Bootstrap__*` and `Jwt__*` values reach the app, profile `run`, command `dotnet run --project src/AuthService.Api`, `ASPNETCORE_URLS=http://+:8080`, publishes `${DEV_API_PORT:-8081}:8080` for humans only); service `tools` (build `Dockerfile.tools`, no ports, joins the default network); named volumes with explicit names `authservice-dev-data`, `authservice-data`, `nuget-cache` (depends on T002, T003)
- [X] T006 **Validate environment**: `cp .env.example .env` and set `HOST_PROJECT_DIR`; confirm `docker compose config` fails with the explanatory message when `HOST_PROJECT_DIR` is unset; then `docker compose build dev tools`; `docker compose run --rm dev dotnet --info` shows SDK major 10; `docker compose run --rm dev dotnet ef --version` prints a version; `docker compose run --rm dev ls /workspace` lists `compose.yaml` (mount points at the repository); `docker compose run --rm dev sh -c 'touch /data/.w && rm /data/.w'` succeeds as the non-root user; `docker compose run --rm tools sqlite3 --version`, `openssl version` and `curl --version` succeed (depends on T004, T005)
- [X] T007 [P] Create `global.json` pinning the .NET 10 SDK with `rollForward: latestFeature`, `Directory.Build.props` (`Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `AnalysisLevel=latest-recommended`, `WarningsAsErrors` for security analyzer rules CA2100, CA3075, CA5350, CA5351, CA5379, CA5400, CA5401), and `Directory.Packages.props` with central package management enabled
- [X] T008 Create the solution and projects through Docker: `AuthService.slnx`; classlib `src/AuthService.Domain`, `src/AuthService.Application`, `src/AuthService.Infrastructure`; web project `src/AuthService.Api` (minimal API template, no controllers); xunit projects `tests/AuthService.IntegrationTests` and `tests/AuthService.ArchitectureTests`; add all to the solution (depends on T006, T007). Do NOT create `AuthService.Application.Tests` (Plan §Constitution Check)
- [X] T009 Establish architectural project references only: Application → Domain; Infrastructure → Application, Domain; Api → Application, Infrastructure; IntegrationTests → Api, Infrastructure; ArchitectureTests → Domain, Application, Infrastructure, Api. Domain references nothing (depends on T008)
- [X] T010 Add package references (versions in `Directory.Packages.props`, latest .NET 10 compatible): Infrastructure gets `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.IdentityModel.JsonWebTokens`; Api gets `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Microsoft.EntityFrameworkCore.Design`; Application gets `Microsoft.Extensions.Logging.Abstractions` only (FR-031 logging); Domain gets nothing; IntegrationTests gets `Microsoft.AspNetCore.Mvc.Testing` and `Microsoft.Extensions.TimeProvider.Testing` (for `FakeTimeProvider`; `TimeProvider` is the only time abstraction, no custom clock interface is introduced); ArchitectureTests gets `NetArchTest.Rules` (depends on T009)
- [X] T011 **Validate Phase 1**: `docker compose run --rm dev dotnet restore`, `dotnet build --no-restore`; both succeed with zero errors; remove template placeholder files (`WeatherForecast`, `Class1.cs`, sample tests) (depends on T010)

**Checkpoint**: A Docker-only workflow restores and builds an empty four-layer solution and the helper tools run.

---

## Phase 2: Persistence and Identity Foundation (Blocking)

**Purpose**: Database, Identity, fixed roles, password rule, bootstrap slice, migrations. Blocks all later phases.

- [X] T012 [P] [US2] Create `src/AuthService.Domain/Roles/AppRoles.cs`: canonical constants `Administrator`, `Manager`, `Operator`, `All` read-only list, and `TryParse(string?, out string canonicalName)` using exact (ordinal) matching; no framework references
- [X] T013 [P] [US3] Create `src/AuthService.Domain/Security/PasswordPolicy.cs`: constants `MinLength = 8`; `Validate(string?)` returns the list of violated rule names (min length, upper-case, lower-case, digit) and never the password; single source of the password rule (spec FR-015); no framework references
- [X] T014 [P] Create `src/AuthService.Infrastructure/Persistence/DatabaseOptions.cs` (`Path` default `/data/auth.db`, `DataProtectionKeysPath` default `/data/keys`, `MigrateOnStartup` default `false`) bound from section `Database`, and `src/AuthService.Infrastructure/Identity/IdentityAccountOptions.cs` (`ResetCodeHours` default 24 allowed 1 to 72, `LockoutMaxFailedAttempts` default 5, `LockoutMinutes` default 15) bound from section `Identity`, both validated on start
- [X] T015 Create `src/AuthService.Infrastructure/Identity/ApplicationUser.cs : IdentityUser<Guid>` adding `IsEnabled` (bool, default `true`) and `CreatedAt` (UTC `DateTimeOffset`); email is the only user-facing identifier and `UserName` is set equal to email internally (data-model User)
- [X] T016 Create `src/AuthService.Infrastructure/Persistence/AuthDbContext.cs : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` with a unique index on `NormalizedEmail` (data-model Indices) (depends on T015)
- [X] T017 Create `src/AuthService.Infrastructure/Persistence/AuthDbContextFactory.cs` (design-time `IDesignTimeDbContextFactory`) using the development database path so `dotnet ef` works in the dev container (depends on T016)
- [X] T018 Create `src/AuthService.Infrastructure/DependencyInjection.cs` with `AddInfrastructure(IConfiguration)` registering `AuthDbContext` (SQLite from `DatabaseOptions.Path`), Identity (`RequireUniqueEmail=true`; `PasswordOptions` mirroring the `PasswordPolicy` constants, RequireNonAlphanumeric false; lockout from `IdentityAccountOptions`; `DataProtectionTokenProviderOptions.TokenLifespan` from `ResetCodeHours`), `AddDataProtection().PersistKeysToFileSystem(DatabaseOptions.DataProtectionKeysPath)`, `TimeProvider.System`; no `EnsureCreated` anywhere (depends on T014, T016)
- [X] T019 Create `src/AuthService.Infrastructure/Identity/RoleSeeder.cs`: idempotently ensure exactly the roles in `AppRoles.All` exist (create missing only, never delete or rename); expose `SeedAsync` (depends on T012, T018)
- [X] T020 [US6] Define the narrow ports used by bootstrap in `src/AuthService.Application/Abstractions/`: `IUserDirectory` with `CountAsync()`, `FindByEmailAsync(email)` and `GetByIdAsync(id)` (read only, both return facts: id, email, `IsEnabled`, roles; needed from Phase 4 by the refresh handler), and `IUserCreator` with `CreateAsync(email, password, roles, isEnabled)` that creates the user and its role memberships atomically (one transaction) and reports `Created`, `EmailAlreadyExists` or `Rejected(reasons)` (never echoing the password)
- [X] T021 [US6] Implement `src/AuthService.Infrastructure/Identity/UserDirectory.cs` (including `GetByIdAsync`) and `UserCreator.cs` over `UserManager<ApplicationUser>`/`AuthDbContext`: mechanics only (query, transactional create); no policy decisions; set `CreatedAt`; map Identity duplicate-email errors to `EmailAlreadyExists` (depends on T018, T020)
- [X] T022 [US6] Create the Application slice `src/AuthService.Application/Features/Bootstrap/BootstrapAdministrator/` (`BootstrapAdministratorCommand(Email, Password)`, handler, outcome enum `Created`, `SkippedUsersExist`, `SkippedNotConfigured`, `InvalidConfiguration`): the handler makes the decisions: if `CountAsync() > 0` return `SkippedUsersExist` and do nothing; if email or password is missing return `SkippedNotConfigured`; if `PasswordPolicy.Validate` reports violations return `InvalidConfiguration`; otherwise call `IUserCreator.CreateAsync(email, password, [AppRoles.Administrator], true)` (depends on T012, T013, T020)
- [X] T023 [US6] Create `src/AuthService.Api/Hosting/BootstrapOptions.cs` (`AdminEmail`, `AdminPassword`, section `Bootstrap`) and `BootstrapHostedService.cs`: after migrations and role seeding, call the handler, log the outcome without secrets (`Bootstrap administrator created`, `Bootstrap skipped: users already exist`); outside Development, fail startup with a clear message when the outcome is `SkippedNotConfigured` or `InvalidConfiguration` (depends on T022)
- [X] T024 Wire `src/AuthService.Api/Program.cs` as composition root: `AddInfrastructure`, register the bootstrap handler and hosted service; `--migrate` switch (when `args` contains `--migrate`, build only the database services, with no signing, bootstrap, CORS or other option validation, apply pending EF migrations, run `RoleSeeder.SeedAsync`, then exit 0, nonzero on failure); an optional ASP.NET Core key-per-file configuration source at `/run/secrets/config` (standard configuration provider, no new abstraction) lets secret values such as `Jwt__Signing__CertificatePassword` and `Bootstrap__AdminPassword` be supplied as mounted files; on a normal start apply migrations only when `Database:MigrateOnStartup` is true, otherwise check for pending migrations or a missing schema and fail with the clear message `Database schema is not up to date; run the migrate service`, then run `RoleSeeder.SeedAsync` (idempotent) before the bootstrap hosted service; option validation (`ValidateOnStart`) runs before the schema check; no endpoints yet (depends on T019, T021, T023)
- [X] T025 Create the initial migration through Docker: `docker compose run --rm dev dotnet ef migrations add InitialIdentity --project src/AuthService.Infrastructure --startup-project src/AuthService.Api --output-dir Persistence/Migrations` and keep the generated files (depends on T017, T024)
- [X] T026 **Validate Phase 2**: `docker compose run --rm dev dotnet build`; `docker compose run --rm dev dotnet run --project src/AuthService.Api -- --migrate`; `docker compose run --rm -T -v authservice-dev-data:/data:ro tools sqlite3 /data/auth.db 'select Name from AspNetRoles'` shows exactly three roles; repeat `--migrate` and confirm no duplicates; then source `.env` in the shell (`set -a; . ./.env; set +a`) and run `docker compose run --rm -e Bootstrap__AdminEmail -e Bootstrap__AdminPassword dev timeout 25 dotnet run --project src/AuthService.Api` so the same credentials are reused in T038, query `select Email from AspNetUsers` (one row) and `select count(*) from AspNetUserRoles`; run again with `-e Bootstrap__AdminEmail=other@example.test` and confirm no second user is created; also confirm that a normal start against an empty database fails with the schema message by running `docker compose run --rm -e Database__MigrateOnStartup=false -e Database__Path=/tmp/empty.db dev dotnet run --project src/AuthService.Api` (the `dev` service defaults to migrating on startup, so the override is required) (depends on T025)

**Checkpoint**: A migrated SQLite database exists on the dev volume with seeded roles and a working, self-closing bootstrap.

---

## Phase 3: Authentication and JWT (US1, US2 claims)

**Goal**: A valid enabled user signs in with email and password and receives an asymmetrically signed
access token that consumers can verify with public material only.

**Independent Test**: Sign in as the bootstrap Administrator, decode the token, confirm `sub`, `role`,
`iss`, `aud`, `iat`, `exp` and `kid`, fetch the JWKS, and confirm wrong password, unknown email and a
disabled account return an identical 401.

### Implementation

- [X] T027 [P] [US1] Create `src/AuthService.Application/Abstractions/IAccessTokenIssuer.cs` (`Issue(userId, roles)` returning the token string and expiry) and `ICredentialVerifier.cs` (`CheckAsync(email, password)` returning facts only: `UserFound`, `PasswordValid`, `IsLockedOut`, `IsEnabled`, `UserId`, `Email`, `Roles`)
- [X] T028 [P] [US1] Create `src/AuthService.Infrastructure/Tokens/JwtOptions.cs`: `Issuer`, `Audience`, `AccessTokenMinutes` default 15 allowed 1 to 60, `Signing:PrivateKeyPemPath`, `Signing:CertificatePath`, `Signing:CertificatePassword`, `PreviousPublicKeyPaths`; validator requires issuer and audience, exactly one signing source, RSA key of at least 2048 bits, and a PFX password supplied through configuration whenever a PFX path is set (never a literal in any committed file); `ValidateOnStart` (Plan §R1, §R11; spec FR-034)
- [X] T029 [US1] Create `src/AuthService.Infrastructure/Tokens/SigningKeyProvider.cs`: load the current RSA private key from PEM or PFX (never exposed outside the class), compute `kid` as base64url SHA-256 of the public key, load previous public keys (PEM or certificate public part), expose RS256 `SigningCredentials` and the list of public `JsonWebKey`s; at startup log the `kid` and, for certificates, the expiry date, without enforcing validity dates (spec FR-010, FR-031). In Development only, when no key is configured, generate a throwaway key under `/data/dev-keys`; in any other environment fail startup with a clear, secret-free message when the key or PFX is absent, unreadable, not loadable with the supplied password, or too weak; never log the password or key material (spec FR-034) (depends on T028)
- [X] T030 [US1] Create `src/AuthService.Infrastructure/Tokens/JwtAccessTokenIssuer.cs` implementing `IAccessTokenIssuer`: claims exactly `sub`, one `role` per role, `iss`, `aud`, `iat`, `exp` and no others (spec FR-007); header `kid`; use `TimeProvider` (depends on T027, T029)
- [X] T031 [US1] Create `src/AuthService.Infrastructure/Identity/CredentialVerifier.cs` implementing `ICredentialVerifier` with `UserManager<ApplicationUser>`: find by email; when not found verify the password against a fixed dummy hash and return `UserFound=false`; otherwise check the password through Identity, record failures and success with the Identity lockout counters, and report `IsLockedOut`, `IsEnabled` and roles as data. It MUST NOT decide whether sign-in succeeds (depends on T027, T018)
- [X] T032 [P] [US1] Create the Login slice in `src/AuthService.Application/Features/Authentication/Login/`: `LoginRequest(Email, Password)`, `LoginResponse(AccessToken, ExpiresInSeconds)` (refresh fields added in T043), `LoginHandler`: validation rejects empty/malformed email and empty password without mentioning account existence; calls `ICredentialVerifier`; succeeds only when `UserFound && PasswordValid && IsEnabled && !IsLockedOut`, otherwise returns the single generic failure and logs a failed-sign-in event without email or password (FR-031) (depends on T027)
- [X] T033 [US1] Create `src/AuthService.Api/Security/JwtBearerSetup.cs` configuring JWT Bearer with explicit validation of issuer, audience, signature (public keys from `SigningKeyProvider`, matched by `kid`) and lifetime (clock skew 1 minute), `MapInboundClaims=false`, `NameClaimType="sub"`, `RoleClaimType="role"` (depends on T029)
- [X] T034 [US2] Create `src/AuthService.Api/Security/AuthorizationPolicies.cs` defining policy `RequireAdministrator` using `AppRoles.Administrator`; no string literal role names elsewhere (depends on T012)
- [X] T035 [US1] Create `src/AuthService.Api/Features/Authentication/AuthEndpoints.cs` with `MapAuthEndpoints` route group `/api/auth`: `POST /login` using `TypedResults`, 200 with `LoginResponse`, 401 Problem Details titled `Invalid credentials` for the generic failure, 400 for validation (depends on T032)
- [X] T036 [P] [US1] Create `src/AuthService.Infrastructure/Tokens/JwksBuilder.cs` producing the JWKS document (public RSA parameters only, `kid`, `alg=RS256`, `use=sig`) and `src/AuthService.Api/Features/Metadata/JwksEndpoint.cs` mapping anonymous `GET /.well-known/jwks.json` with `Cache-Control: public, max-age=300` (depends on T029)
- [X] T037 [US1] Register Application handlers, Infrastructure services, authentication, authorization and endpoints in `src/AuthService.Api/Program.cs` (`UseAuthentication`/`UseAuthorization` before endpoints) (depends on T033, T034, T035, T036)
- [X] T038 **Validate Phase 3**: `docker compose build dev`; start detached `docker compose --profile run up -d dev-api` (it reads the bootstrap variables from `.env`, the same credentials used in T026); wait for `http://dev-api:8080/.well-known/jwks.json` through `tools`; through `tools curl` perform login with the correct password (200), wrong password, unknown email and a disabled account (disable it with `docker compose run --rm -T --user "${LOCAL_UID:-1000}:${LOCAL_GID:-1000}" -v authservice-dev-data:/data tools sqlite3 /data/auth.db "update AspNetUsers set IsEnabled=0 where Email='...'"`), confirming identical 401 status and body; base64-decode the token header and payload and confirm `kid`, `sub`, `role`, `iss`, `aud`, `iat`, `exp` and no other claims; confirm the JWKS contains no `d`, `p` or `q` fields (depends on T037)

**Checkpoint**: Sign-in works end to end with asymmetric tokens and a published public key.

---

## Phase 4: Session Renewal (US4)

**Goal**: Rotating, revocable, hashed refresh tokens with reuse detection and per-session logout.

**Independent Test**: Sign in, renew once, reuse the first refresh token (rejected and session revoked),
sign in again, log out, and fail to renew.

### Implementation

- [X] T039 [P] [US4] Create `src/AuthService.Domain/Sessions/RefreshToken.cs`: fields `Id`, `FamilyId`, `UserId`, `TokenHash`, `CreatedAt`, `ExpiresAt`, `SessionStartedAt`, `SessionExpiresAt`, `State` (`Active`, `Rotated`, `Revoked`), `ReplacedByTokenId`, `RevokedAt`, `RevokedReason` (`Logout`, `ReuseDetected`, `UserDisabled`, `PasswordReset`); `SessionStartedAt`/`SessionExpiresAt` are set at sign-in and copied unchanged on rotation; methods `Rotate(now, replacementId)` allowed only when `Active` and neither the token nor the session is expired, producing a replacement whose `ExpiresAt` is capped at `SessionExpiresAt`, `Revoke(reason, now)`, and `IsUsable(now)`; no framework references (data-model RefreshToken)
- [X] T040 [P] [US4] Create narrow ports in `src/AuthService.Application/Abstractions/`: `IRefreshTokenStore` (create session, find by token hash, rotate atomically: mark old `Rotated` and insert the new one in the same family, revoke family) and `IUserSessionRevoker` (`RevokeAllAsync(userId, reason)`)
- [X] T041 [P] [US4] Create `src/AuthService.Infrastructure/Sessions/SessionOptions.cs` (bounds owned by spec FR-016: `RefreshTokenDays` default 7 allowed 1 to 90; `SessionMaxDays` default 30 allowed 1 to 365; the validator requires `SessionMaxDays >= RefreshTokenDays`, `ValidateOnStart`) and `RefreshTokenGenerator.cs` producing 32 random bytes base64url and a SHA-256 hash helper (hash stored, raw value never stored)
- [X] T042 [US4] Create `src/AuthService.Infrastructure/Sessions/RefreshTokenStore.cs` (implements `IRefreshTokenStore` and `IUserSessionRevoker`) on `AuthDbContext` with a transaction for rotation; add `DbSet<RefreshToken>` and `src/AuthService.Infrastructure/Persistence/RefreshTokenConfiguration.cs` with unique index on `TokenHash`, indices on `FamilyId` and `UserId`, FK to users (depends on T039, T040, T041)
- [X] T043 [US4] Extend the Login slice: `LoginResponse` gains `RefreshToken` and `RefreshTokenExpiresAt`; `LoginHandler` creates a new session family through `IRefreshTokenStore` (setting `SessionStartedAt` and `SessionExpiresAt` from `SessionOptions`) and returns the raw refresh token once (depends on T032, T042)
- [X] T044 [P] [US4] Create `src/AuthService.Application/Features/Authentication/RefreshToken/` (`RefreshTokenRequest`, `RefreshTokenHandler`): look up by hash; unknown token → generic unauthorized; then check the session's absolute expiration FIRST: if `SessionExpiresAt` has passed, reject as an expired session (generic unauthorized) even when the token is `Rotated` or `Revoked`, with no reuse-family revocation work and no reuse event (a generic refresh-failure log without token details is allowed); only for a non-expired session continue: load the user through `IUserDirectory.GetByIdAsync` (disabled-user → generic unauthorized), reject an expired token, and for a `Rotated` or `Revoked` token revoke the whole family with `ReuseDetected`, log the reuse event (no token value or hash) and return unauthorized; success → rotate (the new token's expiry is capped at the session's absolute expiration) and issue a new access token plus new refresh token (depends on T040, T027, T021)
- [X] T045 [P] [US4] Create `src/AuthService.Application/Features/Authentication/Logout/` (`LogoutRequest`, `LogoutHandler`): revoke only the presented token's family with reason `Logout`; unknown or already revoked token still succeeds (idempotent 204) (depends on T040)
- [X] T046 [US4] Add `POST /api/auth/refresh` and `POST /api/auth/logout` to `src/AuthService.Api/Features/Authentication/AuthEndpoints.cs` per contracts/http-api.md (200/401 and 204) (depends on T043, T044, T045)
- [X] T047 [US4] Add migration `AddRefreshTokens` through Docker (`dotnet ef migrations add AddRefreshTokens ...`) and apply it with `docker compose run --rm dev dotnet run --project src/AuthService.Api -- --migrate` (depends on T042)
- [X] T048 **Validate Phase 4**: restart `dev-api`; with `tools curl`, sign in, refresh (new tokens differ), reuse the first refresh token (401), confirm the latest token of that session is now also rejected, sign in again, call logout, then refresh (401); confirm a second session of the same user was unaffected; inspect the table with `tools sqlite3` and confirm `TokenHash` values are hashes and no raw token appears (depends on T046, T047)

**Checkpoint**: Session renewal and revocation behave exactly as clarified.

---

## Phase 5: Current Identity and User Administration (US5, US3, US2)

**Goal**: Authenticated users see their identity; Administrators manage users, roles and password
resets; Managers and Operators are refused.

**Independent Test**: As Administrator create a user, assign and remove roles, disable and enable,
issue and redeem a reset code; as Manager and Operator confirm 403 on every administration route.

### Implementation

- [X] T049 [US3] Extend and add narrow ports in `src/AuthService.Application/Abstractions/`: extend `IUserDirectory` with `ListAsync(skip, take)` and `CountEnabledInRoleAsync(role)` (`GetByIdAsync` already exists from T020); add `IUserStateStore` (`SetEnabledAsync`), `IUserRoleStore` (`GetRolesAsync`, `AddAsync`, `RemoveAsync`) and `IPasswordResetCodes` (`IssueAsync(userId)` returning code and expiry, `RedeemAsync(email, code, newPassword)` returning `Succeeded` or `InvalidCode`); all return facts or outcomes and take no policy decisions
- [X] T050 [US3] Implement the new ports in `src/AuthService.Infrastructure/Identity/`: extend `UserDirectory.cs` with list and count; add `UserStateStore.cs`, `UserRoleStore.cs`, `PasswordResetCodes.cs` (Identity `GeneratePasswordResetTokenAsync` / `ResetPasswordAsync`, lifespan from `IdentityAccountOptions.ResetCodeHours`, single-use through the security stamp, invalid, expired, used and unknown-email cases all return `InvalidCode`); mechanics only (depends on T021, T049)
- [X] T051 [P] [US5] Create `src/AuthService.Application/Features/Identity/GetCurrentUser/` (`CurrentUserResponse(Id, Email, Roles)`, handler taking the `Guid userId` as argument from the endpoint; unknown or disabled user → unauthorized) (depends on T049)
- [X] T052 [P] [US3] Create `src/AuthService.Application/Features/Users/ListUsers/` (`skip` default 0, `take` default 50 maximum 200; response `{ items: UserDto[], total, skip, take }`; `UserDto(Id, Email, IsEnabled, Roles, CreatedAt)`) and `GetUser/` (404 when absent) (depends on T049)
- [X] T053 [US3] Create `src/AuthService.Application/Features/Users/CreateUser/` (`CreateUserRequest(Email, Password, Roles)`): the handler requires at least one role, validates every role with `AppRoles.TryParse`, validates the password with `PasswordPolicy`, rejects a malformed email, checks `IUserDirectory.FindByEmailAsync` for a duplicate (409; also maps `EmailAlreadyExists` from the creator), then calls `IUserCreator.CreateAsync(..., isEnabled: true)`; response is `UserDto` (depends on T049, T012, T013, T021)
- [X] T054 [P] [US3] Create `src/AuthService.Application/Features/Users/EnableUser/` and `DisableUser/`: disabling is refused (conflict) when the target is the last enabled Administrator (decided from `CountEnabledInRoleAsync(AppRoles.Administrator)` and the target's roles); disabling calls `IUserSessionRevoker.RevokeAllAsync(userId, UserDisabled)` and logs the event with actor and target ids (depends on T049, T040)
- [X] T055 [P] [US2] Create `src/AuthService.Application/Features/Roles/AssignRole/` and `RemoveRole/`: the role value must pass `AppRoles.TryParse` else a validation outcome (400); assignment is idempotent; removal is refused when it is the user's last role or would leave zero enabled Administrators (409); log role changes with actor and target ids; roles are never created (depends on T049, T012)
- [X] T056 [P] [US3] Create `src/AuthService.Application/Features/Users/IssuePasswordReset/`: returns the reset code once with `ExpiresAt`; unknown user → 404; an Administrator MAY issue a code for a disabled user, issuing it does not enable the account, and redemption stays forbidden while the account is disabled (the generic failure of T057 applies; see spec FR-014); logs issuance with actor and target ids, never the code (depends on T049)
- [X] T057 [P] [US3] Create `src/AuthService.Application/Features/Authentication/ResetPassword/` (`ResetPasswordRequest(Email, ResetCode, NewPassword)`): validate `NewPassword` with `PasswordPolicy` first (400, independent of code and account state), then look up the account through `IUserDirectory.FindByEmailAsync`; an unknown or disabled account → the same generic unauthorized outcome as an invalid code, without calling `RedeemAsync` (so the code is not consumed); otherwise call `IPasswordResetCodes.RedeemAsync`; `InvalidCode` → the same identical unauthorized outcome; on success call `IUserSessionRevoker.RevokeAllAsync(userId, PasswordReset)` and log the redemption (depends on T049, T040)
- [X] T058 [US5] Create `src/AuthService.Api/Features/Identity/MeEndpoints.cs` mapping `GET /api/me` with `RequireAuthorization()`; read `sub` from the principal and pass it to the handler (depends on T051)
- [X] T059 [US3] Create `src/AuthService.Api/Features/Users/UserEndpoints.cs` mapping a route group `/api/users` with `RequireAuthorization(AuthorizationPolicies.RequireAdministrator)` containing: `GET /`, `GET /{id}`, `POST /` (201 with `Location`), `POST /{id}/enable`, `POST /{id}/disable`, `POST /{id}/password-reset`, `PUT /{id}/roles/{role}`, `DELETE /{id}/roles/{role}`, using explicit DTOs and `TypedResults` per contracts/http-api.md (depends on T052, T053, T054, T055, T056)
- [X] T060 [US3] Add `POST /api/auth/reset-password` (anonymous, 204/400/401) to `src/AuthService.Api/Features/Authentication/AuthEndpoints.cs` and register all new handlers, adapters and endpoint groups in `src/AuthService.Api/Program.cs` and `AddInfrastructure` (depends on T050, T057, T058, T059)
- [X] T061 **Validate Phase 5**: restart `dev-api`; through `tools curl` cover: `/api/me` with and without token; Administrator creates Manager and Operator accounts; Manager and Operator get 403 on `GET /api/users`; `PUT /api/users/{id}/roles/Root` returns 400; removing a user's only role returns 409; disabling the only Administrator returns 409; issue reset code, redeem it, old sessions fail, new password signs in; weak new password returns 400 (depends on T060)

**Checkpoint**: All functional endpoints exist and authorization is enforced by policy.

---

## Phase 6: OpenAPI, Scalar and Runtime Hardening (US8 and cross-cutting)

**Purpose**: Documentation modes, error handling, configuration safety, CORS, throttling, health, and the
production container.

- [X] T062 [P] [US8] Create `src/AuthService.Api/OpenApi/OpenApiSetup.cs`: `AddOpenApi` with a document transformer adding the bearer security scheme, titles and descriptions, and operation transformers marking anonymous versus authorized routes; document request/response DTOs (never Identity entities); map `/openapi/v1.json`
- [X] T063 [US8] **Verify Scalar**: inside Docker, locate the restored `Scalar.AspNetCore` package (restored by T011) (`docker compose run --rm dev sh -c 'ls /nuget/scalar.aspnetcore/*/lib/*/'`), read its XML documentation or inspect `ScalarOptions` members, and record in `specs/001-central-auth-service/research.md` (R9) the exact option names available for hiding the test-request client, proxy and default HTTP client, and which fallback (1, 2 or 3) will be used (depends on T011)
- [X] T064 [US8] Create `src/AuthService.Api/OpenApi/ScalarSetup.cs`: Development maps Scalar at `/scalar` with interactive requests enabled and bearer auth selectable; Production maps Scalar with the mechanism chosen in T063 so the page has no send/execute control, no server URL and no credential input, `servers` removed from the production OpenAPI document if fallback 3 is used, no example tokens, secrets or internal URLs (depends on T062, T063)
- [X] T065 [P] Create `src/AuthService.Api/Security/ProblemDetailsSetup.cs`: `AddProblemDetails`, `UseExceptionHandler` mapping unhandled exceptions to generic 500 Problem Details without stack traces or internals outside Development, `UseStatusCodePages`, consistent 401/403/404/409/429 bodies
- [X] T066 [P] Create `src/AuthService.Api/Security/StartupValidation.cs` and wire `ValidateOnStart` for `JwtOptions` (including PFX file and password loadability), `SessionOptions` (absolute session lifetime >= individual lifetime), `DatabaseOptions`, `IdentityAccountOptions`, `BootstrapOptions`, `CorsOptions`, `RateLimitOptions`; outside Development an invalid or absent signing configuration stops startup with a clear non-secret message, and `Database:MigrateOnStartup=true` is rejected outside Development (the `--migrate` path of T024 skips this validation)
- [X] T067 [P] Create `src/AuthService.Api/Security/CorsSetup.cs` (spec FR-032): `Cors:AllowedOrigins` from configuration; empty list means no cross-origin access in Production; no wildcard origin with credentials; Development may add a documented localhost default
- [X] T068 [P] Create `src/AuthService.Api/Security/RateLimitingSetup.cs`: fixed-window per remote IP policies for `POST /api/auth/login`, `/api/auth/refresh`, `/api/auth/reset-password` (limits from `RateLimit` options, default 10 requests per minute, spec FR-021), rejection returns 429 Problem Details; apply with `RequireRateLimiting`
- [X] T069 [P] Create `src/AuthService.Api/Features/Metadata/HealthEndpoint.cs` (spec FR-033): anonymous `GET /health` runs a database connectivity check and returns 200 `Healthy` or 503 `Unhealthy` with no further detail
- [X] T070 Wire T062, T064 to T069 into `src/AuthService.Api/Program.cs` in the correct middleware order (exception handler, forwarded headers only when configured, CORS, rate limiter, authentication, authorization, endpoints) (depends on T062, T064, T065, T066, T067, T068, T069)
- [X] T071 Add a `--healthcheck` switch to `src/AuthService.Api/Program.cs` that performs an HTTP GET to `http://localhost:8080/health` and exits 0 or 1, so the runtime image needs no curl (depends on T069)
- [X] T072 Create the production `Dockerfile`: stage 1 `mcr.microsoft.com/dotnet/sdk:10.0` restores and publishes `src/AuthService.Api` with `--no-self-contained`; stage 2 `mcr.microsoft.com/dotnet/aspnet:10.0` copies the publish output only, declares `ARG APP_UID=10001` and `ARG APP_GID=10001` only as safe fallbacks for direct `docker build` runs (Compose always overrides them from `.env`), creates an empty `/data` in the build stage and copies it with `--chown=${APP_UID}:${APP_GID}` (no shell or user database needed, and no image-default UID such as 1654 assumed), sets `USER ${APP_UID}:${APP_GID}` (numeric) before `VOLUME /data`, sets `ASPNETCORE_URLS=http://+:8080`, `HEALTHCHECK CMD ["dotnet","AuthService.Api.dll","--healthcheck"]`; no private keys, `.env` or databases are copied (depends on T071)
- [X] T073 Extend `compose.yaml` with `auth-api` (build `Dockerfile` with build args `APP_UID: ${APP_UID:?set APP_UID in .env}` and `APP_GID: ${APP_GID:?set APP_GID in .env}` and `user: "${APP_UID}:${APP_GID}"`, with no hard-coded fallback values in `compose.yaml`, port `${API_PORT:-8080}:8080`, volume `authservice-data:/data`, key material via read-only bind mount of `${SIGNING_KEY_DIR:-${HOST_PROJECT_DIR}/secrets}` (absolute host path, never relative) to `/run/secrets/signing` (mounted `:ro`; the PFX password, when used, comes from `.env` or an orchestrator secret and is never a literal in `compose.yaml`; production deployments SHOULD prefer mounted secret files or the platform's secret mechanism for the PFX password and the bootstrap password, optionally mounted read-only at `/run/secrets/config`), environment from `.env`, `ASPNETCORE_ENVIRONMENT=Production` by default) and a one-shot `migrate` service using the same image with command `--migrate`, the same volume, the same `user:` and the same non-secret environment, with no signing-key mount and no PFX password (depends on T072)
- [X] T074 **Validate Phase 6**: `docker compose build`; start `auth-api` in Production without a signing key and confirm it exits with the clear signing-configuration message (option validation runs before the schema check); load `.env` into the shell (`set -a; . ./.env; set +a`) and run `mkdir -p secrets`; generate a throwaway validation key (owned by the developer identity; this is not the runtime identity, which stays `APP_UID:APP_GID`) with `docker compose run --rm -T --user "${LOCAL_UID:-1000}:${LOCAL_GID:-1000}" -v "${SIGNING_KEY_DIR:-${HOST_PROJECT_DIR}/secrets}:/out" tools sh -c 'openssl genrsa -out /out/signing.pem 2048 && chmod 0644 /out/signing.pem'` (a validation-only throwaway key, exempt from the permission rule of spec FR-034; real permissions are documented in T093) and set `Jwt__Signing__PrivateKeyPemPath` to `/run/secrets/signing/signing.pem`; prove the runtime identity: `docker image inspect --format '{{.Config.User}}' $(docker compose images -q auth-api)` prints exactly the `APP_UID:APP_GID` values from `.env` and `docker compose config` shows the same `user:` for `auth-api` and `migrate`; run `docker compose run --rm migrate`; `docker compose up -d auth-api`; through `tools curl` check `/health`, `/openapi/v1.json` and `/scalar`; confirm the production page has no send/execute control, no server URL and no credential input; `docker compose down` then `up -d` and confirm the earlier user still signs in; confirm Development `dev-api` `/scalar` allows executing a request (depends on T070, T073)

**Checkpoint**: The service is hardened and runnable from the production image with persistent storage.

---

## Phase 7: Minimal Verification

**Purpose**: The smallest suite that proves the validation gate. Do not add tests beyond this list.

- [ ] T075 Create the integration fixture `tests/AuthService.IntegrationTests/AuthServiceFactory.cs`: `WebApplicationFactory<Program>` using a temp-file SQLite database, migrations applied, a generated RSA PEM key written to a temp path, fixed issuer and audience, and helper methods that create an enabled user per role (roles taken from `AppRoles`) and a disabled user through the real Identity stack; the fixture sets all configuration itself and clears ambient `Bootstrap__*`/`Jwt__*` environment values; it replaces the `TimeProvider` registration with a `FakeTimeProvider` exposed to tests so time can be advanced deterministically (depends on T070)
- [ ] T076 [P] [US1] `tests/AuthService.IntegrationTests/LoginTests.cs`: valid credentials return a token; wrong password and unknown email return the same status and body; disabled user cannot sign in (depends on T075)
- [ ] T077 [P] [US1] `tests/AuthService.IntegrationTests/JwtTests.cs`: validate an issued token with an independent `JwtBearer`-style validation configured only with the public key from the JWKS endpoint; wrong audience and wrong issuer are rejected; expected `role` claims present and no unexpected claims (depends on T075)
- [ ] T078 [P] [US2] `tests/AuthService.IntegrationTests/AuthorizationTests.cs`: Administrator succeeds on `GET /api/users`; Manager and Operator receive 403; unauthenticated receives 401 (depends on T075)
- [ ] T079 [P] [US2] `tests/AuthService.IntegrationTests/RoleAssignmentTests.cs`: assigning a supported role succeeds; an unsupported role is rejected with 400 and nothing changes; removing a user's last role and disabling the last enabled Administrator are refused (depends on T075)
- [ ] T080 [P] [US4] `tests/AuthService.IntegrationTests/RefreshTokenTests.cs`: renewal returns new tokens; reusing the rotated token fails and revokes the session; logout revokes the session; redeeming an administrator-issued reset code revokes the user's sessions and fails for a disabled user without consuming the code; a renewal after the session's absolute lifetime is rejected and a rotation never extends the session past it (advance the fixture's `FakeTimeProvider`; no sleeps and no wall-clock dependence) (depends on T075)
- [ ] T081 [P] [US6] `tests/AuthService.IntegrationTests/BootstrapTests.cs`: bootstrap creates the Administrator only when the store is empty and does nothing when a user already exists (depends on T075)
- [ ] T082 [P] [US3] `tests/AuthService.IntegrationTests/PersistenceTests.cs`: create a user, dispose the host, start a new host on the same database file and confirm the user, roles and a refresh-token record are still present (real Identity + EF Core + SQLite) (depends on T075)
- [ ] T083 [P] `tests/AuthService.ArchitectureTests/DependencyRulesTests.cs` using `NetArchTest.Rules`: Domain has no reference to Infrastructure, ASP.NET Core, EF Core or Identity; Application has no reference to Infrastructure or Api (four checks, nothing more)
- [ ] T084 **Validate Phase 7**: `docker compose run --rm dev dotnet test`; all tests pass; confirm no coverage collection or threshold configuration exists anywhere in the repository (depends on T076 to T083)

**Checkpoint**: The minimum validation gate (spec FR-029, constitution XI) is automated and green in Docker.

---

## Phase 8: Documentation and Final Container Validation (US7, US8)

**Purpose**: Everything a new operator or developer needs, then a clean-room validation through Docker.

- [ ] T085 [P] [US7] Create `README.md`: purpose, quick start (Docker only), repository map, links to `docs/`
- [ ] T086 [P] [US7] Create `docs/architecture.md`: layers, allowed dependency directions, vertical slice layout, narrow ports, where business decisions live versus adapter mechanics, diagram of request flow
- [ ] T087 [P] [US7] Create `docs/development.md`: assumes Docker and no local .NET; start dev environment, restore, build, test, create and apply migrations, run the app (`dev-api`), HTTP checks and database inspection through `tools`, stop and remove containers and volumes; how to find `HOST_PROJECT_DIR` when the developer or agent itself runs in a container (for example `docker inspect` of its own container's mounts) and why published ports are not used; that `LOCAL_UID`/`LOCAL_GID` are development-only and unrelated to the production `APP_UID`/`APP_GID`
- [ ] T088 [P] [US7] Create `docs/authentication.md`: login, refresh, logout and reset flows with request/response examples, uniform failure behavior, lockout and rate limits (5/15 minutes, 10 per minute), password rule, reset codes (issuance for a disabled account is allowed; redemption fails while it stays disabled; a failed redemption does not consume the code), refresh-token individual lifetime and absolute session lifetime (sliding but bounded; sign in again at the cap), TLS expectation (terminate TLS in a reverse proxy; never expose the service directly to untrusted networks)
- [ ] T089 [P] [US7] Create `docs/authorization.md`: fixed role set, policy usage, Administrator-only operations, last-Administrator and last-role guards, trust model including that an Administrator who issues a reset code can take over that account, acceptable lag for role and disable changes (token lifetime)
- [ ] T090 [P] [US7] Create `docs/jwt-consumer-integration.md`: an independent ASP.NET Core consumer configuring JWT Bearer with issuer, audience, `kid`-aware key resolution via JWKS or a static public key, `NameClaimType="sub"`, `RoleClaimType="role"`, `MapInboundClaims=false`; authorization policy examples for Administrator, Manager and Operator; the key-retrieval behavior from contracts/token-and-jwks.md (cached key set with a maximum age of 5 minutes and re-fetch when older; optional forced refresh on an unknown `kid` at most once per minute per process; a successful refresh replaces the cached set; a failed refresh may keep using the last fetched set; fail closed with none); rotation handling; emergency key replacement and its two limitations (static-key consumers keep accepting tokens signed with the compromised key until updated by hand or automation; a consumer that cannot reach the key endpoint may keep its last fetched set, so removal is not guaranteed during an outage); replace, never merge, the cached key set on refresh; clock skew; refresh tokens never go to consumers
- [ ] T091 [P] [US7] Create `docs/configuration.md`: every setting (`Jwt__*`, `Session__*` (including `RefreshTokenDays`, `SessionMaxDays` and the rule absolute >= individual), `Identity__*` including `ResetCodeHours`, lockout values, `Bootstrap__*`, `Database__*` including `DataProtectionKeysPath`, `Cors__*`, `RateLimit__*`), the deployment variables `APP_UID`/`APP_GID` (authoritative `.env` or deployment configuration, documented defaults in `.env.example`) and the separate development-only `LOCAL_UID`/`LOCAL_GID`, defaults, allowed ranges, required in Production, which ones cause startup failure
- [ ] T092 [P] [US7] Create `docs/database.md`: SQLite location `/data/auth.db`, volumes (`authservice-data` production, `authservice-dev-data` development), data-protection key ring at `/data/keys` and why it must be backed up, migrations (create, apply, failure handling and restore path), backup (stop the service, copy database and key ring using the `tools` image or `docker cp`) and restore procedure with verification, single-instance limitation
- [ ] T093 [P] [US7] Create `docs/deployment.md`: production container deployment, required external configuration, private signing material handling per spec FR-034 (read-only mounted secret; numeric runtime identity `APP_UID:APP_GID` taken from `.env` or the deployment configuration, which is the authoritative operational source when Compose is used (documented default 10001:10001 in `.env.example`; the Dockerfile ARG defaults are only fallbacks for direct builds), applied to the image and Compose and used in the provisioning commands, and distinct from the development-only `LOCAL_UID`/`LOCAL_GID`; owner operator, group `APP_GID`, mode 0640, directory 0750 (setting the group needs operator privileges); readable only by the service process and the provisioning identity; the container never needs root; documented fallback for rootless Docker or userns-remap: a runtime-readable secret supplied by the deployment mechanism, or an operator-owned 0600 secret copied into a container-visible location owned by the runtime user before startup; a production key path outside the checkout, since the default `secrets/` directory is for local validation only; never in an image or the repository, PFX password only from external secret configuration and never a literal in Dockerfiles, Compose files, logs or generated documentation; secret handling guidance: a `.env` that contains secrets must have mode 0600 where supported and stay out of version control, production SHOULD prefer mounted secret files (key-per-file source at `/run/secrets/config`) or the deployment platform's secret mechanism for the PFX password and the bootstrap Administrator password, and environment variables may be visible through container inspection depending on the runtime, which is an accepted limitation when `.env` variables are used, clear startup failure on missing or invalid credentials), public key distribution to consumers, certificate provisioning and creation of a development certificate, expiration (not enforced, logged at startup, monitor and rotate before expiry), rotation overview, Scalar/OpenAPI production behavior, TLS termination, reverse proxy forwarded headers
- [ ] T094 [P] [US7] Create `docs/operations.md`: health checks, logs and which security events appear (spec FR-031), first Administrator bootstrap (how configured, when permitted, how initialization is detected, how to confirm it is closed, advice to remove the bootstrap password), migrations (including the `Database schema is not up to date` startup message and the `--migrate` mode), planned key/certificate rotation step by step (with overlap) and the separate emergency replacement procedure of spec FR-035 (new `Current` key, remove the compromised public key from the published set with no overlap, restart; JWKS consumers drop it within 5 minutes after the service stopped publishing it, provided their refresh succeeds (cache maximum age 5 minutes, replace not merge); the guarantee does not hold while a consumer cannot reach the key endpoint, and static-key consumers keep accepting tokens signed with it until updated; state both limitations explicitly; treat all tokens signed with the compromised key as untrusted once consumers are updated; sessions are not destroyed automatically), backup and recovery, common authentication failures (401 causes, 403 causes, expired token, wrong audience, unknown `kid`, clock skew, locked-out user, rate limited)
- [ ] T095 **Final validation (clean state)**: remove local build output and volumes (`docker compose down -v`), then `docker compose build` and confirm success (depends on T072, T073, T085 to T094)
- [ ] T096 **Final validation (restore/build/test)**: run `docker compose run --rm dev dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build` and confirm all succeed; record the actual output summary in the task notes (depends on T095)
- [ ] T097 **Final validation (migrations)**: create a throwaway migration with `docker compose run --rm dev dotnet ef migrations add ValidationProbe ...`, confirm it is generated, remove it with `dotnet ef migrations remove`, then apply the real migrations with `docker compose run --rm migrate` (depends on T096)
- [ ] T098 **Final validation (startup and smoke)**: start `auth-api` with bootstrap values and a generated key; execute every step of `specs/001-central-auth-service/quickstart.md` through the `tools` service (login, `/api/me`, role denial, refresh rotation and reuse, logout, bootstrap closed on restart, persistence across `docker compose down` and `up`, JWKS validation, Development versus Production Scalar behavior); time the login request (expect well under 2 seconds, SC-001) and time the first-Administrator procedure from a clean volume (expect under 15 minutes, SC-007); run the documented backup and restore drill from `docs/database.md` (stop `auth-api`, copy the database file and key ring with the `tools` image into a backup, create a throwaway volume, restore into it, start a second service instance on that volume) and confirm 100% of users, roles and enabled states are present and sign-in works (SC-012); validate emergency key replacement (spec FR-035) against the production service: (1) fetch `/.well-known/jwks.json` through `tools curl` and record the active `kid` of key A; (2) generate a second throwaway key B with the `tools` service and switch `Jwt__Signing__PrivateKeyPemPath` to it while leaving `PreviousPublicKeyPaths` empty (the compromised key is removed, not kept); (3) restart with `docker compose up -d --force-recreate auth-api` and wait for `/health`; (4) fetch the JWKS again and confirm key A's `kid` is absent, (5) key B's `kid` is present and a fresh login token carries it, and (6) neither response contains `d`, `p`, `q`, `dp`, `dq` or `qi`; consumer-cache behavior is not simulated here and stays documented in T090 and T094; record real results (depends on T097)
- [ ] T099 **Final validation (Docker-only compliance)**: confirm `which dotnet`, `which sqlite3`, `which openssl` fail in the agent container; confirm no task or document invokes `dotnet`, `curl` against published ports, or ad-hoc `docker run` of public images; list in the task notes any `specs/001-central-auth-service/checklists/security.md` items that are now resolved by the artifacts (the reviewer owns the checkboxes and they are not edited) (depends on T098)

---

## Requirement Traceability

Every requirement maps to at least one task. Where a task does not name its requirement, this table is the
authority.

| Requirement | Tasks |
|---|---|
| FR-001 sole credential owner | T015, T031, T090 |
| FR-002 fixed roles | T012, T019, T055, T079 |
| FR-003 email login | T032, T035, T076 |
| FR-004 user fields, at least one role | T015, T016, T053, T055 |
| FR-005 identical failure | T031, T032, T035, T076 |
| FR-006 disabled users | T032, T044, T054, T076 |
| FR-007 token and lifetime | T028, T030, T077 |
| FR-008 independent verification | T033, T036, T077, T090 |
| FR-009 asymmetric key, key exclusivity | T028, T029, T073, T074 |
| FR-010 public keys, rotation, expiry date | T029, T036, T093, T094 |
| FR-011 current identity | T051, T058 |
| FR-012 user administration | T052 to T059, T078 |
| FR-013 server-side enforcement | T034, T059, T078 |
| FR-014 password reset (incl. disabled account) | T050, T056, T057, T080 |
| FR-015 password rule and confidentiality | T013, T018, T053, T057 |
| FR-016 refresh token and session lifetimes | T039 to T043, T044, T066, T080 |
| FR-017 single use and reuse | T044, T080 |
| FR-018 logout and disable | T045, T054, T080 |
| FR-019 bootstrap | T020 to T023, T081, T094 |
| FR-020 last administrator and last role | T054, T055, T079 |
| FR-021 throttling | T014, T031, T068 |
| FR-022 error format | T065 |
| FR-023 reference docs | T062, T064 |
| FR-024 development and production docs | T063, T064, T074 |
| FR-025 persistence and migrations | T024, T025, T092, T097 |
| FR-026 startup validation | T066, T074 |
| FR-027 operational docs | T085 to T094 |
| FR-028 Docker-only | T002 to T011, T095 to T099 |
| FR-029 minimal tests | T075 to T084 |
| FR-030 new consumer integration | T090 |
| FR-031 security event logging | T023, T029, T032, T044, T054 to T057 |
| FR-032 CORS | T067 |
| FR-033 health | T069, T071 |
| FR-034 private key protection (incl. numeric runtime identity) | T004, T028, T029, T066, T072, T073, T074, T093 |
| FR-035 emergency key replacement | T036, T090, T093, T094, T098 |
| SC-001 sign-in time | T098 |
| SC-002 identical refusals | T076 |
| SC-003 independent validation | T077 |
| SC-004 Administrator-only | T078 |
| SC-005 unsupported roles | T079 |
| SC-006 refresh token reuse | T048, T080 |
| SC-007 first Administrator | T081, T098 |
| SC-008 production docs read-only | T064, T074 |
| SC-009 Docker-only | T095 to T099 |
| SC-010 minimal suite | T084 |
| SC-011 consumer integration docs | T090 |
| SC-012 restore from backup | T092, T098 |

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1** (Setup): T001 to T004 and T007 run in parallel; then T005, T006, T008 to T011 sequentially
- **Phase 2** (Foundation): depends on Phase 1; blocks all later phases
- **Phase 3** (US1/US2 claims): depends on Phase 2
- **Phase 4** (US4): depends on Phase 3 (login and token issuer)
- **Phase 5** (US5/US3/US2): depends on Phase 4 (disable and reset revoke sessions) and Phase 3 (policy)
- **Phase 6** (hardening): depends on Phase 5 for complete endpoints; T062 and T063 may start after Phase 3
- **Phase 7** (tests): depends on Phase 6 wiring (T070); test files run in parallel
- **Phase 8** (docs and validation): docs T085 to T094 may start once Phase 5 is complete; final validation depends on everything

### User Story Dependencies

- **US1** (sign-in): foundation only
- **US2** (roles): `AppRoles` in Phase 2, policy in Phase 3, role endpoints in Phase 5
- **US6** (bootstrap): slice and hosted service in Phase 2 (T020 to T023), exercised from Phase 3 on
- **US4** (renewal): after US1
- **US5, US3**: after US1 and US4
- **US7, US8**: after the behavior they document

### Within Each Phase

- Domain and port definitions before adapters, adapters before handlers, handlers before endpoints, endpoints before wiring, wiring before the validation task
- Tasks editing the same file (for example `UserDirectory.cs`, `AuthEndpoints.cs`, `Program.cs`, `compose.yaml`) are never marked [P]

### Parallel Opportunities

- Phase 1: T001, T002, T003, T004, T007
- Phase 2: T012, T013, T014
- Phase 3: T027, T028, T032 (after T027), T036
- Phase 4: T039, T040, T041, then T044, T045
- Phase 5: T051, T052, T054, T055, T056, T057 after T049
- Phase 6: T062, T065, T066, T067, T068, T069
- Phase 7: T076 to T083 after T075
- Phase 8: T085 to T094

---

## Parallel Example: Phase 5

```bash
# After T049 (ports defined), these touch different slice folders:
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
3. **Stop and validate** (T038): token issued, signature verifiable with the JWKS, generic failures
4. Run T075 to T077 early as a smoke gate if desired

### Incremental Delivery

1. Add Phase 4 for sessions, validate with T048
2. Add Phase 5 for administration, validate with T061
3. Add Phase 6 hardening and the production image, validate with T074
4. Phase 7 tests, then Phase 8 documentation and the clean-room validation

### Notes

- Each validation task must be executed for real; failures are fixed before the next phase starts
- Commit after each task or logical group
- Do not add tests, endpoints or features beyond those listed
- Checklist `checklists/security.md` is reviewer-owned; implementation never ticks its boxes
