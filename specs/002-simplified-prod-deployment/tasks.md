---

description: "Task list for Simplified Production Deployment"
---

# Tasks: Simplified Production Deployment

**Input**: `specs/002-simplified-prod-deployment/` — spec.md (incl. Clarifications), plan.md, research.md, data-model.md, quickstart.md, contracts/configuration.md

**Tests**: Requested by FR-023 — focused, risk-based, no coverage target; existing authentication/authorization tests are not duplicated.

**Completion rule**: A task is complete ONLY when its implementation is done AND its stated validation passes. Writing code alone never completes a task. All .NET commands run through Docker (`docker compose run --rm dev ...`; constitution IX). Nothing is installed locally.

## Format: `- [ ] T### [P?] [US?] Description with file path`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- Story labels: **US1** simple deploy (P1) · **US2** persistence/stable signing (P1) · **US3** advanced opt-ins & compatibility (P2) · **US4** documentation (P2)
- Phases follow the requested delivery order; phases 2–4 are blocking foundations for US1/US2.

---

## Phase 1: Configuration & Compose split (US1, US3)

- [ ] T001 [US1] Create `compose.dev.yaml` containing the `dev`, `dev-api` and `tools` services moved verbatim from `compose.yaml` (including their `HOST_PROJECT_DIR`/`LOCAL_UID`/`LOCAL_GID`/`DEV_API_PORT` usage and the `authservice-dev-data` and `nuget-cache` volumes). Validate: with `COMPOSE_FILE=compose.yaml:compose.dev.yaml` and `HOST_PROJECT_DIR=$PWD`, `docker compose config --services` lists dev, dev-api, tools, auth-api, migrate.
- [ ] T002 [US1] Edit `compose.yaml` to contain only production services `auth-api` and `migrate` plus volumes `authservice-data` and an empty `authservice-signing-none` volume; delete the moved dev/tools services and dev-only volumes. Validate: in a shell with `HOST_PROJECT_DIR`, `LOCAL_UID`, `LOCAL_GID`, `DEV_API_PORT`, `APP_UID`, `APP_GID`, `SIGNING_KEY_DIR` all unset and `COMPOSE_FILE` unset, `docker compose config` succeeds and `docker compose config --services` prints exactly `auth-api` and `migrate`.
- [ ] T003 [US1] In `compose.yaml` `auth-api`/`migrate`: replace `APP_UID: "${APP_UID:?...}"` / `APP_GID` and `user:` with `${APP_UID:-10001}` / `${APP_GID:-10001}`. Validate: `docker compose config` with APP_UID/APP_GID unset shows `user: 10001:10001` and build args 10001; with `APP_UID=20001 APP_GID=20001` shows 20001.
- [ ] T004 [US1] In `compose.yaml` `auth-api`: remove the `${SIGNING_KEY_DIR:-${HOST_PROJECT_DIR}/secrets}` bind mount; mount `${SIGNING_KEY_DIR:-authservice-signing-none}:/run/secrets/signing:ro`. Validate: `docker compose config` with no `SIGNING_KEY_DIR` shows the named volume; with `SIGNING_KEY_DIR=/abs/secrets` shows a bind to that path; neither references `HOST_PROJECT_DIR`.
- [ ] T005 [US1] In `compose.yaml` `auth-api`: add `Database__MigrateOnStartup: "${Database__MigrateOnStartup:-false}"` to `environment`; keep `ASPNETCORE_ENVIRONMENT: Production`, port `${API_PORT:-8080}:8080`, volume `authservice-data:/data`, and the existing health check from the image. Leave `migrate` with `Database__MigrateOnStartup: "false"` and `command: ["--migrate"]`, and add `profiles: ["migrate"]` to `migrate` so `docker compose up` never starts it (it would race an auto-migrating `auth-api` on the same SQLite file); an explicit `docker compose run --rm migrate` still runs profile-gated services. Validate: `docker compose config` shows these values with and without `Database__MigrateOnStartup` exported, and `docker compose config --services` (no `--profile`) prints only `auth-api`.
- [ ] T006 [P] [US1] Create `.env.production.example` with ONLY: `Bootstrap__AdminEmail=`, `Bootstrap__AdminPassword=`, `Jwt__Issuer=`, `Jwt__Audience=`, `Cors__AllowedOrigins__0=`, `API_PORT=8080`, `Database__MigrateOnStartup=true`, with short comments (secret handling, mode 0600, TLS-at-proxy note). It MUST NOT mention `HOST_PROJECT_DIR`, `LOCAL_UID`, `LOCAL_GID`, `DEV_API_PORT`, `APP_UID`, `APP_GID`, `SIGNING_KEY_DIR`, or any PEM/PFX key. Validate: `grep -E 'HOST_PROJECT_DIR|LOCAL_|DEV_API_PORT|APP_UID|APP_GID|SIGNING_KEY_DIR|PrivateKey|Certificate' .env.production.example` returns nothing.
- [ ] T007 [P] [US3] Edit `.env.example` (development): add `COMPOSE_FILE=compose.yaml:compose.dev.yaml`; keep `HOST_PROJECT_DIR`, `LOCAL_UID`, `LOCAL_GID`, `DEV_API_PORT`; move `APP_UID`/`APP_GID`/`SIGNING_KEY_DIR` comments to advanced/optional wording. Validate: `cp .env.example .env`, set `HOST_PROJECT_DIR=$PWD`, then `docker compose config --services` lists dev, dev-api, tools, auth-api, migrate and `docker compose build dev tools` succeeds.
- [ ] T008 [P] [US1] Ensure `.gitignore` ignores `.env` (and not the two example files) and `.dockerignore` excludes `.env`, `secrets/` and `*.pem`/`*.pfx`. Validate: `git check-ignore .env` succeeds; `git check-ignore .env.production.example` fails; `docker build` context listing (`docker build --no-cache` then `find / -name '*.pem'` in image, deferred to T074) shows none.
- [ ] T009 [US1] Verify `Dockerfile` needs no change: `/data` pre-created and chowned to `${APP_UID}:${APP_GID}` defaults 10001, `USER` non-root, `HEALTHCHECK` retained. Validate: `docker build -t authsvc-check .` then `docker run --rm --entrypoint id authsvc-check` shows uid 10001 and `docker image inspect` shows the HEALTHCHECK. Edit only if a check fails.
- [ ] T010 [US1] Verify `docker compose up` selects only production services: with a minimal `.env` derived from `.env.production.example` run `docker compose up --no-start --build` and `docker compose ps -a --services`; only `auth-api` is created (`migrate` is profile-gated per T005); confirm `docker compose run --rm migrate` still runs it despite the profile.

**Checkpoint**: `docker compose config` is clean with a bare environment; dev workflow still renders via `COMPOSE_FILE`.

---

## Phase 2: Shared database initialization (US1, US3)

- [ ] T011 [US1] Create `src/AuthService.Infrastructure/Persistence/DatabaseInitializer.cs`: one class with (a) `MigrateAsync()` = `Database.MigrateAsync()` + `PRAGMA journal_mode=DELETE;`, (b) `EnsureCurrentAsync()` = existing schema-currentness check (`CanConnectAsync` false or any pending migration → `InvalidOperationException("Database schema is not up to date; run the migrate service")`), (c) `SeedRolesAsync()` via existing `RoleSeeder`, and (d) `InitializeAsync(bool migrate)` composing them. Register in `src/AuthService.Infrastructure/DependencyInjection.cs` (scoped). Validate: solution builds via `docker compose run --rm dev dotnet build --no-restore`.
- [ ] T012 [US1] Edit `src/AuthService.Api/Program.cs`: replace both `ApplyMigrationsAndSeedAsync` and `PrepareDatabaseAsync` bodies with calls to `DatabaseInitializer` (`--migrate` → migrate + seed roles; startup → `InitializeAsync(options.MigrateOnStartup)`); delete the duplicated logic. Validate: `grep -n "MigrateAsync" src` shows exactly one occurrence (inside `DatabaseInitializer`) and `grep -n "journal_mode" src` exactly one.
- [ ] T013 [US1] Edit `src/AuthService.Api/Security/StartupValidation.cs`: delete `DatabaseStartupValidator` and its registration so Production may enable `MigrateOnStartup`; confirm `DatabaseOptions.MigrateOnStartup` (`src/AuthService.Infrastructure/Persistence/DatabaseOptions.cs`) stays `bool` defaulting to `false` (absent = false). Validate: build passes; covered by tests T052–T055.
- [ ] T014 [US3] Verify the `--migrate` path (`Program.cs`) still returns before `ValidateStartupOptions`/signing/bootstrap, so the `migrate` service needs no JWT, signing or bootstrap configuration. Validate: `docker compose run --rm migrate` against a fresh volume with a bare environment (no Jwt/Bootstrap/Cors variables) exits 0 and the schema exists.
- [ ] T015 [US1] Verify failures abort startup: confirm in `Program.cs` that exceptions from `DatabaseInitializer` are not caught (process exits non-zero before `app.Run()`). Validate: covered by T054; manual spot-check by pointing `Database__Path` at an unwritable location and observing a non-zero exit.

**Checkpoint**: one migration implementation, both modes call it.

---

## Phase 3: Auto-signing key lifecycle (US1, US2, US3)

Create `src/AuthService.Infrastructure/Tokens/PersistedSigningKey.cs` incrementally (T016–T024), then wire (T025–T030).

- [ ] T016 [US2] Add to `src/AuthService.Infrastructure/Tokens/JwtOptions.cs` `SigningOptions`: `string? AutoKeyPath` (when unset, the effective path is `<directory of Database.Path>/signing/signing.pem`, i.e. `/data/signing/signing.pem` in production and the test data root under tests; overridable) and `bool AllowKeyRegeneration` (default `false`). Validate: build passes; default path behavior exercised by T043/T044 (data root) and `AllowKeyRegeneration=false` default by T045.
- [ ] T017 [US2] In `PersistedSigningKey`: create the signing directory if absent with `UnixFileMode` 0700 (`UserRead|UserWrite|UserExecute`) via `Directory.CreateDirectory(path, mode)`. Validate: test T043 asserts directory mode `0700` on Linux.
- [ ] T018 [US2] In `PersistedSigningKey`: generate the key with `RSA.Create(3072)`; serialize with `ExportPkcs8PrivateKeyPem()` (PKCS#8 PEM, header `BEGIN PRIVATE KEY`). No OpenSSL/process invocation. Validate: test T043 loads the file with `RSA.ImportFromPem`, `KeySize == 3072`, header is `BEGIN PRIVATE KEY`.
- [ ] T019 [US2] In `PersistedSigningKey`: write atomically — create a temp file in the same directory with `FileMode.CreateNew` and `UnixCreateMode = UserRead|UserWrite` (0600), write+flush, then move into place WITHOUT overwrite (`File.Move(temp, target, overwrite:false)`); if the target appeared meanwhile, delete temp and load the existing key. Always delete the temp file on failure. Validate: test T043 asserts file mode `0600` and no `*.tmp` leftovers; T047 asserts an existing file's bytes are never changed.
- [ ] T020 [US2] In `PersistedSigningKey`: load/reuse — if the target file exists, parse with `ImportFromPem`; require `KeySize >= 2048`; return it (kid computed by existing `SigningKeyProvider.ComputeKeyId`). Validate: T044 (restart reuses; kid identical).
- [ ] T021 [US2] In `PersistedSigningKey`: failure paths — file exists but is unreadable, malformed, not RSA, or < 2048 bits → throw `InvalidOperationException` with a secret-free message (no key material, no password) and NEVER write/rename/delete the file. Validate: T046 (bytes unchanged, startup fails).
- [ ] T022 [US2] In `PersistedSigningKey`: decision input — `EnsureAsync(bool usersExist, bool allowRegeneration)`: file absent and `!usersExist` → generate (first provisioning); file absent and `usersExist` and `!allowRegeneration` → throw `InvalidOperationException` ("Persisted signing key is missing but users exist; restore the key from backup or set Jwt__Signing__AllowKeyRegeneration=true to deliberately replace it") ; file absent and `usersExist` and `allowRegeneration` → generate. Validate: T045, T048.
- [ ] T023 [US2] In `PersistedSigningKey`: recovery logging — while `AllowKeyRegeneration=true`, log a Warning on EVERY startup (regardless of whether the key is present) stating the flag is enabled and should be removed; when a replacement is actually generated log a distinct Warning "signing key replaced; previously issued access tokens will no longer validate" including only the new kid. Use `[LoggerMessage]` like the existing `SigningKeyProvider.Log` class. Validate: T048 captures logs and asserts both messages and absence of PEM markers.
- [ ] T024 [US2] Guarantee the private key is never logged or surfaced: no log argument, exception message, or OpenAPI/endpoint output includes key text or file contents. Validate: T048/T043 log assertions plus `grep -rn "ExportPkcs8PrivateKeyPem\|ExportRSAPrivateKeyPem" src` shows only the file-write site.
- [ ] T025 [US3] Edit `src/AuthService.Api/Security/StartupValidation.cs` `JwtOptionsValidator`: in non-Development, PEM and PFX both set → fail ("Only one JWT signing source may be configured"); `CertificatePath` set without `CertificatePassword` → fail; `CertificatePassword` set without `CertificatePath` → fail; explicit source configured but file missing/unreadable/<2048 → fail (existing behavior kept); neither configured → VALID (auto mode, Production). Development behavior unchanged. Validate: T049–T051.
- [ ] T026 [US2] Edit `src/AuthService.Infrastructure/Tokens/SigningKeyProvider.cs`: add the auto branch — when neither PEM nor PFX is configured in non-Development, obtain the key from the singleton `PersistedSigningKey` (already ensured at startup) instead of throwing "JWT signing key is required"; explicit PEM/PFX branches and `PreviousPublicKeyPaths` unchanged; Development generator branch unchanged. Register `PersistedSigningKey` as a singleton in `DependencyInjection.cs`. Validate: builds; T044.
- [ ] T027 [US3] Verify precedence: with PEM or PFX configured, `PersistedSigningKey.EnsureAsync` is never called and `/data/signing` is not created or touched (even if a generated key already exists). Validate: T049/T050 assert no file creation and kid == explicit key's kid.
- [ ] T028 [US2] Verify JWKS stability: `SigningKeyProvider.KeyId` for an auto key equals the same value across two host lifetimes using the same data directory, and `/.well-known` JWKS output (see `src/AuthService.Api/Features/Metadata`) lists it. Validate: T044 compares JWKS bodies byte-for-byte across `Restart()`.

**Checkpoint**: key lifecycle complete in isolation.

---

## Phase 4: Startup orchestration, bootstrap, CORS (US1)

- [ ] T029 [US1] Edit `src/AuthService.Api/Program.cs` startup sequence before `app.Run()`: `ValidateStartupOptions` → `DatabaseInitializer.InitializeAsync` (migrate-if-enabled/else currentness check, then roles) → signing initialization (resolve `IUserDirectory.CountAsync() > 0` in a scope, then `PersistedSigningKey.EnsureAsync(usersExist, options.Signing.AllowKeyRegeneration)` only when no explicit PEM/PFX) → eager `GetRequiredService<SigningKeyProvider>()` → bootstrap run INLINE (`await bootstrap.RunAsync()` from `Program.cs`, no hosted service, so ordering does not depend on hosted-service/Kestrel start order). Remove `AddHostedService<BootstrapHostedService>()`. Add a code comment stating why signing precedes bootstrap. Validate: T056.
- [ ] T030 [US1] Remove the both-or-neither `.Validate(...).ValidateOnStart()` on `BootstrapOptions` in `Program.cs` so partial bootstrap settings no longer fail startup when users exist. Validate: T058.
- [ ] T031 [US1] Convert `src/AuthService.Api/Hosting/BootstrapHostedService.cs` into a plain `BootstrapRunner` (rename file/class; method `RunAsync`, invoked inline per T029; keep the same handler call and log messages): when users exist skip silently with the existing log; when the store is empty and the environment is not Development, missing, partial, invalid-email, or password-policy-invalid configuration throws `InvalidOperationException` with a message that never contains the password or email-password pairing details beyond field names. Keep Development behavior unchanged. Validate: T057–T058 and the existing `BootstrapTests` still pass.
- [ ] T032 [US1] Verify email-format rejection: confirm whether `UserCreator`/Identity rejects a malformed bootstrap email (e.g. `not-an-email`); if it does not, add a minimal format check inside `src/AuthService.Application/Features/Bootstrap/BootstrapAdministrator/BootstrapAdministratorHandler.cs` returning `InvalidConfiguration` (before user creation, after the users-exist check). No Domain/Application→Infrastructure dependency; architecture tests still pass. Validate: T057 plus `docker compose run --rm dev dotnet test tests/AuthService.ArchitectureTests`.
- [ ] T033 [US1] Verify bootstrap atomicity is unchanged: admin creation and role assignment remain in the existing transaction (`UserCreator`); partial failure leaves zero users. Validate: existing `BootstrapTests` still pass.
- [ ] T034 [US1] Edit `src/AuthService.Api/Security/CorsSetup.cs`: in non-Development, add startup validation (`ValidateOnStart`) requiring ≥1 origin; each entry must match `^https?://[^/?#@\s]+$` and parse via `Uri.TryCreate(..., UriKind.Absolute, ...)`; reject `*` anywhere in the value (including wildcard hosts such as `https://*.example.com`), userinfo (`@`), trailing slash, path, query, fragment, non-http(s) scheme; host required, port optional; no normalization; explicitly listed localhost origins are accepted. Development keeps its `http://localhost:4200` fallback and current permissive validation. Validate: T059.
- [ ] T035 [US1] Confirm no request is served before initialization completes: `Program.cs` performs all initialization before `app.Run()`, bootstrap runs inline before `app.Run()` (no hosted-service ordering dependency), and any exception exits non-zero. Validate: T054 (failed migration → host creation throws, no bootstrap user) and quickstart step 4 (container healthy only after init).

**Checkpoint**: startup is fail-fast and ordered.

---

## Phase 5: Compatibility paths (US3)

- [ ] T036 [US3] Existing PEM deployment: with `SIGNING_KEY_DIR=<abs dir>` and `Jwt__Signing__PrivateKeyPemPath=/run/secrets/signing/<file>`, `docker compose up -d` starts and JWKS kid equals the PEM's kid; no `/data/signing` directory created. Validate: quickstart step 13 (T069).
- [ ] T037 [US3] Existing PFX deployment: with `Jwt__Signing__CertificatePath` and `Jwt__Signing__CertificatePassword` (including password supplied via the mounted-secret mechanism already supported by `Program.cs` `AddKeyPerFile("/run/secrets/config")`), startup succeeds. Validate: quickstart step 14 (T069) and unit-level T050.
- [ ] T038 [US3] Manual migration deployment: `Database__MigrateOnStartup=false` (and also omitted) + `docker compose run --rm migrate` then `up -d auth-api` works; omitted/false with an outdated or missing schema makes `auth-api` exit with "run the migrate service". Validate: T055, T070 (quickstart step 15).
- [ ] T039 [US3] Custom `APP_UID/APP_GID`: with `APP_UID=20001 APP_GID=20001` and a fresh volume, `auth-api` generates `/data/signing/signing.pem` owned by 20001 and runs as 20001. Validate: quickstart step 16 (T071).
- [ ] T040 [US3] Production startup without `HOST_PROJECT_DIR`/`LOCAL_UID`/`LOCAL_GID`: `env -u HOST_PROJECT_DIR -u LOCAL_UID -u LOCAL_GID docker compose up -d --build` succeeds with the minimal `.env`. Validate: quickstart steps 17–18.
- [ ] T041 [US3] Development flow through `compose.dev.yaml`: from a dev `.env` (T007) run `docker compose build dev tools`, `docker compose run --rm dev dotnet build --no-restore`, `docker compose run --rm dev dotnet test --no-build`, and `docker compose --profile run up -d dev-api` followed by `docker compose run --rm -T tools curl -fsS http://dev-api:8080/health`; `docker compose --profile run down` afterward. Validate: all commands succeed unchanged.

---

## Phase 6: Focused automated tests (US1, US2, US3)

Reuse `tests/AuthService.IntegrationTests/AuthServiceFactory.cs`. Run: `docker compose run --rm dev dotnet test` (T060 = full suite).

- [ ] T042 [US2] Extend `tests/AuthService.IntegrationTests/AuthServiceFactory.cs` with an optional configuration/environment parameter set: `Production` environment mode, no PEM by default, caller-supplied overrides (Jwt issuer/audience, Cors origin, Bootstrap, Database settings, signing paths), and a shared data root so `Restart()` reuses `/signing`. Existing tests must keep their current default (Development + PEM) unchanged. Validate: `docker compose run --rm dev dotnet test` existing 22 tests still pass.
- [ ] T043 [P] [US2] Create `tests/AuthService.IntegrationTests/SigningKeyAutoProvisionTests.cs` — Test 1: empty install in Production creates `<data>/signing/signing.pem` (PKCS#8, RSA 3072; on Unix dir 0700, file 0600; no temp leftovers; no key text in captured logs).
- [ ] T044 [US2] In the same file — Tests 2–3: after `Restart()` the file bytes are identical, JWKS body and `kid` are identical, and a token issued before restart validates after.
- [ ] T045 [US2] In the same file — Test 4a: create a user, delete the key file, restart → host fails to start with the missing-key error and no key is created.
- [ ] T046 [US2] In the same file — Test 5: corrupt content, unreadable file (chmod 000 when not root; skip with explicit reason if running as root), and <2048-bit key each fail startup; file bytes unchanged afterward.
- [ ] T047 [US2] In the same file — assert an existing valid key file is never overwritten by startup in any mode (byte-compare after first start, restart, and recovery-flag start).
- [ ] T048 [US2] In the same file — Test 4b recovery: users exist + key missing + `Jwt:Signing:AllowKeyRegeneration=true` → starts with a NEW kid and both warnings; with the key present and the flag on → no replacement, still warns; flag on + corrupt key → still fails and file unchanged.
- [ ] T049 [P] [US3] Create `tests/AuthService.IntegrationTests/SigningSourceSelectionTests.cs` — Test 6: explicit PEM configured → kid equals the PEM's; no `<data>/signing` created even if an auto key already exists there.
- [ ] T050 [US3] In the same file — Test 7: explicit PFX (generated self-signed with password in the test) overrides auto-signing; kid equals the certificate key's.
- [ ] T051 [US3] In the same file — Test 8: PEM+PFX both set, PFX path without password, password without path, and configured-but-missing file each fail startup with a secret-free message.
- [ ] T052 [P] [US1] Create `tests/AuthService.IntegrationTests/ProductionStartupTests.cs` — Test 15: the minimal operator configuration (issuer, audience, one CORS origin, bootstrap email/password, `Database:MigrateOnStartup=true`, no signing settings) starts in Production; also assert behaviorally that Production `MigrateOnStartup=true` is accepted and that an absent setting does not auto-migrate (outdated schema still fails).
- [ ] T053 [US1] In the same file — Test 9: with auto-migration on and an empty database file, startup applies all migrations (`GetPendingMigrationsAsync` empty) and the fixed roles exist.
- [ ] T054 [US1] In the same file — Test 10: migration failure (e.g. database path pointing at a directory/unwritable file) → host does not start, no listener, and no bootstrap user exists.
- [ ] T055 [US3] In the same file — Test 16: `MigrateOnStartup=false` on an empty/outdated database → startup fails with "run the migrate service"; after running `DatabaseInitializer.MigrateAsync` (the same routine used by `--migrate`) startup succeeds.
- [ ] T056 [P] [US1] Create `tests/AuthService.IntegrationTests/BootstrapStartupTests.cs` — Test 11: on an empty store, bootstrap creates the Administrator only after migrations and signing init (assert user exists, JWKS available, login works immediately).
- [ ] T057 [US1] In the same file — Test 12: on an empty store each of both-absent, email-only, password-only, invalid email, and policy-violating password fails startup in Production; assert exception/log text never contains the supplied password.
- [ ] T058 [US1] In the same file — Test 13: with an existing user, absent/partial/invalid bootstrap values are ignored (no failure, no new user), and changing bootstrap email/password across restart creates no second user.
- [ ] T059 [P] [US1] Create `tests/AuthService.IntegrationTests/CorsValidationTests.cs` — Test 14 (theory, minimal rows): Production fails for none, `*`, `https://*.example.com`, trailing slash, path, query/fragment, userinfo, non-http(s); passes for `https://app.example.test`, `http://localhost:4200`, and host:port forms; matching is exact (no normalization).
- [ ] T060 [US1] Run the full suite `docker compose run --rm dev dotnet test` (including `AuthService.ArchitectureTests`) — all prior and new tests must pass; no coverage threshold added.

---

## Phase 7: Documentation (US4)

- [ ] T061 [US4] Rewrite `docs/deployment.md` with `# Production deployment` → `## Quick deployment` FIRST (the three commands; the five required values and optional `API_PORT`; named volume `authservice-data`; first login with bootstrap credentials; TLS note: LAN direct OK, Internet-facing behind a TLS-terminating reverse proxy; `docker compose` only, no other command) → `## Advanced deployment` (external PEM/PFX with `SIGNING_KEY_DIR`; explicit migration with `Database__MigrateOnStartup=false` and `docker compose run --rm migrate` (the `migrate` service is profile-gated so `up` never starts it); custom `APP_UID/APP_GID`; secret mounts; recovery key generation via `Jwt__Signing__AllowKeyRegeneration=true` with key-replacement warning; controlled key rotation with `PreviousPublicKeyPaths`; reverse proxy/TLS and `ForwardedHeaders`). Validate: heading order check `grep -n '^## ' docs/deployment.md` shows Quick before Advanced; Quick section contains none of `HOST_PROJECT_DIR`, `LOCAL_UID`, `APP_UID`, `SIGNING_KEY_DIR`, `openssl`, `migrate`.
- [ ] T062 [P] [US4] Update `docs/configuration.md`: the contract table from `contracts/configuration.md` (required vs default), new keys (`Jwt:Signing:AutoKeyPath` default `<Database.Path dir>/signing/signing.pem`, `Jwt:Signing:AllowKeyRegeneration`), `Database__MigrateOnStartup` default semantics (absent=false, template=true), production CORS rules (exact, no normalization), bootstrap rules (empty store only; ignored when users exist), signing source selection/conflict rules, and the list of intentional compatibility changes (explicit `SIGNING_KEY_DIR`, Production CORS now mandatory, dev Compose via `COMPOSE_FILE`).
- [ ] T063 [P] [US4] Update `docs/database.md`: both migration modes, single shared routine, failure behavior (nothing served), manual-mode schema-currentness error, persistent layout table (`/data/auth.db`, `/data/keys`, `/data/signing/signing.pem`, modes), deleting the volume = full reset and key replacement event; backup/restore steps now include `/data/signing`.
- [ ] T064 [P] [US4] Update `docs/operations.md`: backup scope (DB + Data Protection ring + generated key from the same snapshot; external keys under external custody), partial-restore consequences (missing key with users → startup failure; missing ring → protected data invalid; mismatched DB/key not detected), signing-key-loss consequences (old tokens invalid, consumers fetch new JWKS, key replacement event), recovery procedure, health/log checks, optional hardening (remove bootstrap password and recreate; not required).
- [ ] T065 [P] [US4] Update `README.md` (production quick-start link and 3-command summary; dev commands note `COMPOSE_FILE`) and `docs/development.md` (compose file split, `.env.example` `COMPOSE_FILE`, `-f` alternative). Validate: every `docker compose` command in README/docs is runnable against the new files (spot-run `docker compose config` for each distinct form).
- [ ] T066 [US4] Doc consistency pass: `grep -rn "HOST_PROJECT_DIR\|LOCAL_UID\|LOCAL_GID" README.md docs` shows them only in development contexts; no doc tells the operator to run migrate, openssl, chmod/chown, or remove the bootstrap password as a REQUIRED production step.

---

## Phase 8: Final Docker validation (all stories) — follows quickstart.md

Every Phase 8 `docker compose` command uses `-p simplecheck -f compose.yaml -f validation-override.yaml` (the override only renames the data volume `authservice-data` → `simplecheck-data` and may set a spare `API_PORT`; it is created in the scratchpad/untracked and never committed). The data volume is pinned by `name:` in `compose.yaml`, so `-p` alone does NOT isolate it. Record outputs in `specs/002-simplified-prod-deployment/validation.md` (no secrets).

- [ ] T067 [US1] Simple path (quickstart A0–A7): FIRST apply the isolation guard (A0: abort if `docker volume inspect authservice-data` exists on this host unless it is a disposable validation host; use the uncommitted `validation-override.yaml` that renames the data volume to `simplecheck-data` and sets a spare `API_PORT`); clean volume; `cp .env.production.example .env`; fill values; single `docker compose -p simplecheck -f compose.yaml -f validation-override.yaml up -d --build`; `docker compose ps --services` lists ONLY `auth-api`; healthy; bootstrap login 200; `/api/me` shows Administrator; capture JWKS kid from `/.well-known/jwks.json`.
- [ ] T068 [US2] Recreate/persistence (A8–A12): `up -d --force-recreate auth-api`; login again; user and refresh/session state persist; Data Protection ring files in `/data/keys` unchanged; JWKS kid identical; `/data/signing/signing.pem` (volume `simplecheck-data`) present, mode `-rw-------`, owner 10001; users, roles and refresh/session state intact.
- [ ] T069 [US3] Compatibility PEM/PFX (B13–B14): explicit PEM then explicit PFX deployments start with their own kid and no `/data/signing` creation.
- [ ] T070 [US3] Manual migration (B15): `Database__MigrateOnStartup=false` fails fast on a fresh volume; `docker compose run --rm migrate` then `up` succeeds.
- [ ] T071 [US3] Custom UID/GID and env independence (B16–B18): `APP_UID=20001 APP_GID=20001`; startup without `HOST_PROJECT_DIR`; arbitrary `LOCAL_UID/LOCAL_GID` have no effect.
- [ ] T072 [US3] Dev flow (T041) re-run after all changes: build/test/dev-api health through `compose.dev.yaml`.
- [ ] T073 Security (C19–C22): logs contain no `PRIVATE KEY` or bootstrap password; image contains no signing key (`find / -name '*.pem'` in `docker run --entrypoint sh`); `git grep -n "BEGIN .*PRIVATE KEY"` and `git ls-files | grep -E '^\.env$|secrets/'` empty; `docker compose exec auth-api id -u` ≠ 0.
- [ ] T074 Fail-safe (D): delete `signing.pem` with users present → container exits with missing-key error and no key created; corrupt the file → exits, file bytes unchanged; set `Jwt__Signing__AllowKeyRegeneration=true` → starts with new kid and warnings; unset the flag afterward.
- [ ] T075 Cleanup and final gate: `docker compose -p simplecheck -f compose.yaml -f validation-override.yaml down -v` (never without the override, which would target the real `authservice-data` volume); delete `validation-override.yaml`; full `dotnet test` green (T060); update checkboxes only for tasks whose validation passed; summarize results in `validation.md`.

---

## Dependencies & Execution Order

- Phase 1 → independent of C# work; T001→T002→T003/T004/T005 (same file, sequential); T006–T008 parallel.
- Phase 2 (T011→T012→T013) blocks Phase 4 and tests T053–T055.
- Phase 3: T016→T017→T018→T019→T020→T021→T022→T023 (one file, sequential) → T024; T025 (Api validator) parallel with T016–T024; T026 depends on T022; T027–T028 depend on T026.
- Phase 4: T029 depends on T012, T026; T030–T033 parallel with T034; T035 last.
- Phase 6: T042 first; test files T043/T049/T052/T056/T059 are parallel ([P]); tasks within a file sequential; T060 last.
- Phase 7 after behavior is stable (Phases 1–4); T062–T065 parallel.
- Phase 8 requires everything; T067→T068 sequential, others independent after T067.
- Phase 5 tasks are verification steps executed once their implementing phases pass (T036–T039 after Phase 4; T041 after Phase 1).

**Parallel examples**: T006+T007+T008; T043+T049+T052+T056+T059 (separate files); T062+T063+T064+T065.

**MVP**: Phases 1–4 + tests T043–T048, T052–T054, T056–T058 deliver US1+US2 (simple path with persistent signing). US3 (advanced/compat) and US4 (docs) follow; Phase 8 closes everything.

---

## Traceability

| Requirement | Tasks |
|---|---|
| FR-001 | T006, T067 |
| FR-002 | T006, T002, T040, T066 |
| FR-003 | T002–T005, T010, T067 |
| FR-004 | T002, T004, T009, T010 |
| FR-005 | T003, T009, T039, T071, T073 |
| FR-006 | T018, T022, T043 |
| FR-007 | T017–T020, T044, T068 |
| FR-007a | T022, T045, T048, T074 |
| FR-008 | T017, T019, T024, T043, T073 |
| FR-009 | T025, T027, T049–T051, T069 |
| FR-010 / FR-010a | T005, T006, T011–T013, T052, T053 |
| FR-011 | T011, T012, T014, T038, T055, T070 |
| FR-012 | T015, T029, T035, T054, T056 |
| FR-013 / FR-013a | T030–T033, T057, T058 |
| FR-014 | T006, T030, T058, T061, T064 |
| FR-015 | T002, T068, T063 |
| FR-016 / FR-016a | T034, T059, T031, T073 (no weakening: existing suite T060) |
| FR-017 | T006, T008, T024, T073 |
| FR-018 | T061, T065, T066 |
| FR-019 / FR-020 / FR-020a | T063, T064 |
| FR-021 | T061 |
| FR-022 | T036–T039, T062, T069–T072 |
| FR-023 | T043–T060 |
| SC-001 / SC-012 | T010, T067 |
| SC-002 | T053, T067 |
| SC-003 | T056, T067 |
| SC-004 | T044, T068 |
| SC-005 | T028, T044, T068 |
| SC-006 | T003, T009, T071, T073 |
| SC-007 | T002, T040, T071 |
| SC-008 | T036, T037, T049–T051, T069 |
| SC-009 | T038, T055, T070 |
| SC-010 | T008, T024, T073 |
| SC-011 | T061, T066 |
