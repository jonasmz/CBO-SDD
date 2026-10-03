# Research: Simplified Production Deployment

No NEEDS CLARIFICATION remain. Findings from the current code and decisions taken:

## Findings in current code
- `StartupValidation.cs`: Production requires exactly one of PEM/PFX; `DatabaseStartupValidator` rejects `MigrateOnStartup=true` outside Development.
- `Program.cs`: migration logic exists twice (`ApplyMigrationsAndSeedAsync` for `--migrate`, `PrepareDatabaseAsync` for startup); both run `MigrateAsync` + `PRAGMA journal_mode=DELETE` + role seeding.
- `BootstrapOptions` is `ValidateOnStart` with a both-or-neither rule, which fails startup even when users exist (contradicts clarification 3). `BootstrapHostedService` already throws in non-Development for not-configured/invalid on an empty store; bootstrap creation is already transactional with role assignment (`UserCreator`).
- `SigningKeyProvider` is a singleton; non-Development without PEM/PFX throws; Development auto-generates RSA 2048 at `/data/dev-keys/signing.pem` (non-atomic, no permissions).
- `CorsSetup` accepts an empty list in Production.
- `compose.yaml`: `${HOST_PROJECT_DIR:?}` and `${APP_UID:?}` are interpolated at load for the whole file; `dev` and `tools` have no profile, so `docker compose up` would start them; `auth-api` bind-mounts `${SIGNING_KEY_DIR:-${HOST_PROJECT_DIR}/secrets}`. Verified with Compose v5: a `:?` variable inside a profile-gated service still aborts `config`.
- `/data` is pre-created and chowned in the Dockerfile; a fresh named volume inherits that ownership.

## Decisions

| Topic | Decision | Rationale | Alternatives rejected |
|---|---|---|---|
| Key path | `/data/signing/signing.pem` | Same volume as DB and DP ring; one backup boundary | Separate volume (more operator concepts) |
| Format | PKCS#8 PEM (`ExportPkcs8PrivateKeyPem`) | Loadable by the existing `ImportFromPem` path; consistent with explicit PEM support | PFX (needs a password to manage) |
| Size | RSA 3072 | ≥2048 requirement; longer-lived margin; negligible cost for RS256 | 2048 (minimum only) |
| Permissions | dir 0700, file 0600 via `UnixCreateMode`; temp file `CreateNew` + no-overwrite move | Runtime-identity-only read; no partial/overwritten key | chmod after write (race window) |
| Order | migrate → roles → signing → bootstrap | Signing needs "users exist?" and bootstrap creates users | Signing after bootstrap (would always see users) |
| Recovery | `Jwt__Signing__AllowKeyRegeneration` generates only when file absent | Explicit, narrow, never overwrites corrupt key | Marker/one-time token files (key-management subsystem) |
| Compose split | `compose.yaml` prod / `compose.dev.yaml` dev via `COMPOSE_FILE` | Only way to drop `HOST_PROJECT_DIR` from interpolation and stop `up` from starting dev services | Profiles (still interpolated); relaxing `:?` (leaves dev services in `up`) |
| Signing mount | `${SIGNING_KEY_DIR:-authservice-signing-none}` named empty volume | No host path in simple path; advanced users keep bind mounts | Separate override file (breaks existing invocations) |
| Migrate default | App absent=false; template sets true; compose default false | Existing installs unchanged | Compose default true (silently changes existing installs) |
| CORS | Regex + exact match, Production only | Clarification 4; no normalization | `Uri` round-trip (hides trailing slash) |
| Shared migration | `Infrastructure/Persistence/DatabaseInitializer` | One implementation for both modes | Leaving two copies |
