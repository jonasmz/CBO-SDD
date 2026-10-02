# Implementation Plan: Simplified Production Deployment

**Branch**: `002-simplified-prod-deployment` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification and Session 2026-10-02 clarifications (authoritative).

## Summary

Make `cp .env.production.example .env` → edit → `docker compose up -d --build` a complete first install. The work is
deployment mechanics only: (1) auto-provision and persist an RSA signing key when no PEM/PFX is configured,
fail-safe when users exist but the key is missing; (2) allow migrate-on-startup in Production (absent = false) through
one shared migration routine also used by the `migrate` service; (3) tighten Production validation for bootstrap and
CORS; (4) split Compose so the production file has no dev services and no `HOST_PROJECT_DIR`; (5) rewrite docs
(simple first). No Domain/Application/API-contract change.

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core, ASP.NET Core Identity, EF Core + SQLite
**Primary Dependencies**: existing only (no new packages); `System.Security.Cryptography.RSA` for key generation
**Storage**: SQLite `/data/auth.db`, Data Protection ring `/data/keys`, new generated key `/data/signing/signing.pem` — all in named volume `authservice-data`
**Testing**: xUnit integration tests (`WebApplicationFactory`) + architecture tests, run via Docker dev containers; Docker end-to-end validation script of the Compose path
**Target Platform**: Linux container, Docker Compose (single node)
**Project Type**: web-service (existing 4-project solution)
**Performance Goals**: none new
**Constraints**: non-root runtime; no secrets in repo/image; no OpenSSL/shell-out; single instance per volume
**Scale/Scope**: small installations; ~10 source files touched, 1 new Infrastructure class, Compose/Docs/env changes

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I Hexagonal/slices | PASS | Key persistence + migration mechanics in Infrastructure; startup orchestration + validation in API; Domain/Application untouched (bootstrap email-format check, if needed, stays in the existing Bootstrap slice) |
| II Simple design | PASS | One concrete class, no interfaces/frameworks added; no item on the "not without need" list introduced |
| III Authentication | PASS | Unchanged |
| IV Roles | PASS | Unchanged |
| V Token security | PASS | Asymmetric key; private key never logged/exposed/committed/imaged; mechanism configurable without code change |
| VI Persistence | PASS | No silent delete/recreate; migrations still EF; fail-fast on missing key |
| VII/VIII Contracts/OpenAPI | PASS | No change |
| IX Container-first | PASS | All dev/test via Docker (dev services move to `compose.dev.yaml`, wired by `COMPOSE_FILE` in dev `.env`) |
| X Docker reproducibility | PASS | Production runs from image + named volume |
| XI Minimal testing | PASS | ~16 targeted tests; existing gate preserved |
| XII Config/secrets | PASS | Prod fails clearly on missing/invalid mandatory config; dev convenience unchanged |

Post-design re-check: PASS (no new violations; no Complexity Tracking entries needed).

## Architecture Impact

- **Domain**: none.
- **Application**: none expected. Bootstrap email-format validity: today `UserCreator` rejection maps to `InvalidConfiguration`; confirm in implementation that a malformed address (e.g. `abc`) is rejected — if Identity accepts it, add a minimal email-format check inside the existing `BootstrapAdministratorHandler` (same slice).
- **Infrastructure**: new `Tokens/PersistedSigningKey` (generate/load/validate/persist); `SigningKeyProvider` gains the auto branch; new `Persistence/DatabaseInitializer` (single migrate + journal-mode + role-seed routine); `JwtOptions.Signing` gains the recovery flag and `AutoKeyDirectory` default.
- **API/composition root**: `Program.cs` startup ordering, `StartupValidation.cs`, `CorsSetup.cs`, `BootstrapHostedService`/options validation.
- **Docker/Compose**: split compose files, production defaults, env templates.
- **Docs**: see Documentation Plan.

## Design Decisions (details in [research.md](research.md))

1. **Startup order** (before `app.Run`, so Kestrel is not bound and nothing is served):
   validate options → `DatabaseInitializer` (migrate if enabled, else schema-currentness check) → fixed roles → signing-material resolve/create (needs user count, so it MUST precede bootstrap or a fresh install would always look "existing") → eager `SigningKeyProvider` resolve → bootstrap hosted service → ready. Any failure aborts the process.
2. **Signing source**: PEM xor PFX (both → error; PFX path without password or password without path → error; configured missing/unreadable file → error). Any explicit source disables auto mode. Neither → auto mode (Production). Development behavior unchanged.
3. **Auto key**: `/data/signing/signing.pem` (path derived from the data volume; overridable `Jwt:Signing:AutoKeyPath`), PKCS#8 PEM, RSA 3072, dir mode 0700, file mode 0600 created with `CreateNew` to a temp name then atomic no-overwrite rename. Existing file → load; parse/size failure → fail; never rewritten.
4. **Existing-install detection**: `IUserDirectory.CountAsync() > 0` after migrations. Key absent + users exist → fail unless recovery flag set.
5. **Recovery opt-in**: `Jwt__Signing__AllowKeyRegeneration=true` permits generation only when the key file is absent (never over an existing/corrupt file); emits a warning on every start while set and a distinct "key replaced, new kid" event; docs say remove it afterwards. Not in the env template.
6. **Migrations**: absent = false; Production `true` allowed (remove `DatabaseStartupValidator` rejection); `--migrate` CLI and startup both call `DatabaseInitializer`.
7. **Bootstrap**: drop the `ValidateOnStart` "both-or-neither" check (it violates "ignored when users exist"); validation happens in the bootstrap flow only when the store is empty; Production failure on missing/partial/invalid/policy-violating; messages never contain the password.
8. **CORS**: Production-only startup validation: ≥1 entry; each matches `^https?://[^/?#@\s]+$` and parses as absolute; no `*`; exact, no normalization. Development keeps its localhost fallback.
9. **Compose**: `compose.yaml` = production (`auth-api`, `migrate`); `compose.dev.yaml` = `dev`, `dev-api`, `tools`. Reason: Compose interpolates `${HOST_PROJECT_DIR:?}` for every service even when profile-gated (verified), and un-profiled `dev`/`tools` would be started by `up`. `.env.example` (dev) sets `COMPOSE_FILE=compose.yaml:compose.dev.yaml`.
   - `auth-api`: `APP_UID/GID` default 10001; `Database__MigrateOnStartup: ${Database__MigrateOnStartup:-false}` (template sets `true`); signing mount `${SIGNING_KEY_DIR:-authservice-signing-none}:/run/secrets/signing:ro` with an empty declared named volume as the no-op default (no host path needed, existing `SIGNING_KEY_DIR` users unaffected).
   - Image: `/data` pre-created and chowned to build-arg UID/GID so the fresh named volume inherits writable ownership.

## Affected Files

| Area | Files |
|---|---|
| Infrastructure | `Tokens/SigningKeyProvider.cs`, `Tokens/JwtOptions.cs`, new `Tokens/PersistedSigningKey.cs`, new `Persistence/DatabaseInitializer.cs`, `DependencyInjection.cs` |
| API | `Program.cs`, `Security/StartupValidation.cs`, `Security/CorsSetup.cs`, `Hosting/BootstrapHostedService.cs` |
| Application (conditional) | `Features/Bootstrap/BootstrapAdministrator/BootstrapAdministratorHandler.cs` |
| Docker | `compose.yaml`, new `compose.dev.yaml`, `Dockerfile` (verify only), `.dockerignore` |
| Config | new `.env.production.example`, `.env.example` (dev; add `COMPOSE_FILE`), `.gitignore` (`.env`) |
| Tests | new `DeploymentStartupTests.cs`, `SigningKeyAutoProvisionTests.cs`, `CorsValidationTests.cs`; factory parameterization in `AuthServiceFactory.cs` |
| Docs | `README.md`, `docs/deployment.md`, `configuration.md`, `database.md`, `operations.md`, `development.md` (compose file split) |

## Implementation Sequence

1. `DatabaseInitializer` + replace both Program.cs code paths; drop Production rejection of `MigrateOnStartup`.
2. `PersistedSigningKey` + `SigningKeyProvider` auto branch + `JwtOptionsValidator` rules (xor, partial PFX, auto allowed in Production) + recovery flag.
3. Program.cs ordering (migrate → roles → signing → eager provider → bootstrap).
4. Bootstrap validation relocation; Production fail-fast; email-format check if required.
5. Production CORS validation.
6. Compose split, `.env.production.example`, dev `.env.example`, Dockerfile check.
7. Tests (below), run in Docker dev env.
8. Docker end-to-end validation (quickstart.md).
9. Documentation rewrite; secret scan.

## Test Plan (new behavior only; Production-environment factory variant without a PEM)

| # | Test |
|---|---|
| 1-3 | Empty install generates key; restart reuses file; JWKS kid identical |
| 4 | Users exist + key absent → startup fails; with recovery flag → new kid, warning |
| 5 | Corrupt / unreadable key → fails; file bytes unchanged afterward |
| 6-7 | Explicit PEM / PFX wins; no `/signing` file created; kid equals explicit key's |
| 8 | PEM+PFX, PFX-without-password, password-without-path, missing file → fail |
| 9-10 | Auto-migrate brings empty DB current; injected migration failure → host does not start, no bootstrap user |
| 11-13 | Bootstrap runs after init; invalid/partial/weak on empty DB fails w/o leaking password; users exist → partial/invalid config ignored |
| 14 | Prod CORS: none, `*`, trailing slash, path, non-http fail; localhost accepted |
| 15-16 | Minimal template-equivalent Production config starts; `MigrateOnStartup=false` + outdated schema fails; shared initializer works for `--migrate` |

Existing suite untouched; no coverage target.

## Compatibility Plan

| Existing use | Outcome |
|---|---|
| Explicit PEM / PFX / external PFX password | Unchanged semantics; `SIGNING_KEY_DIR` must now be set explicitly (the implicit `${HOST_PROJECT_DIR}/secrets` default is removed) — **intentional breaking change #1** |
| `Database__MigrateOnStartup` omitted/false + `migrate` service | Unchanged (absent=false; compose default false) |
| `MigrateOnStartup=true` in Production | Was rejected, now allowed (relaxation) |
| Custom `APP_UID/APP_GID` | Unchanged; defaults now 10001 instead of required |
| Bootstrap partial config with existing users | Previously failed startup; now ignored — **behavior change #2 (relaxation)** |
| Production with no CORS origins | Previously started; now fails — **intentional breaking change #3** |
| `docker compose run dev|tools` | Requires `COMPOSE_FILE` from dev `.env` (or `-f`) — **change #4**, documented in development guide |
| Plain `docker compose up` previously started dev/tools too | Now only `auth-api` (fix) |

## Risk Analysis

| Risk | Mitigation |
|---|---|
| Accidental key rotation | Fail-fast when users exist; never overwrite; temp+no-overwrite rename; recovery explicit; docs + warning log |
| Permissions under non-root | Image pre-chowns `/data`; key dir 0700/file 0600 created by runtime user; custom UID covered by validation step 16; pre-existing volume owned by another UID → clear IO error |
| Ordering/race | Everything before `app.Run`; signing before bootstrap by necessity; single-instance documented; `CreateNew` guards double generation |
| Existing-install detection | Based on user count post-migration; empty-DB+no-key = first provisioning (documented trade-off: a DB wiped but key kept reuses the key) |
| Production MigrateOnStartup change | Absent=false; template-only `true`; documented |
| Partial restore | Documented consequences; no consistency checker (decision) |
| Compose compatibility | Split verified by `docker compose config` for both file sets; `migrate` kept |
| Template secrets | `.env` git-ignored; template has empty placeholders; secret scan step |

## Docker Validation Plan

See [quickstart.md](quickstart.md) (22 steps mapped to SC-001…SC-012).

## Documentation Plan

- `docs/deployment.md`: `# Production deployment` → `## Quick deployment` (3 commands, 5 required values, API_PORT optional, volume, TLS note, first login) → `## Advanced deployment` (external PEM/PFX + `SIGNING_KEY_DIR`, manual migrations, custom UID/GID, secret mounts, recovery key generation, key rotation, reverse proxy/TLS, removing the bootstrap password as optional hardening).
- `docs/configuration.md`: new/changed keys, defaults table, CORS rules, bootstrap rules.
- `docs/database.md`: both migration modes, shared logic, failure behavior.
- `docs/operations.md`: backup scope (DB + `/data/keys` + `/data/signing`), partial-restore consequences, key-loss consequences, volume deletion = full reset, health/log checks.
- `docs/development.md`, `README.md`: compose file split, quick link to production guide.

## Project Structure

```text
specs/002-simplified-prod-deployment/
├── plan.md  research.md  data-model.md  quickstart.md
└── contracts/configuration.md
src/AuthService.{Api,Infrastructure}/ (+ conditional Application bootstrap slice)
tests/AuthService.IntegrationTests/
compose.yaml  compose.dev.yaml  .env.production.example  .env.example
docs/
```

**Structure Decision**: existing layout; no new projects.
