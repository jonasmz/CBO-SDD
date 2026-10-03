# Configuration reference

ASP.NET Core accepts colon-separated keys; environment variables use double underscores. Invalid validated
values fail startup. Values below are the implemented defaults and ranges.

## What a normal production installation sets

Only installation-specific values are required; everything else has a safe default. `.env.production.example`
contains exactly these:

| Key | Required | Notes |
|---|---|---|
| `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword` | On an empty database only | See "Bootstrap" below |
| `Jwt__Issuer`, `Jwt__Audience` | Yes | Startup fails if empty |
| `Cors__AllowedOrigins__0` | Yes in Production | See "Production CORS" below |
| `API_PORT` | No (8080) | Host port |
| `Database__MigrateOnStartup` | Absent = `false`; `.env.production.example` sets `true` | `dev`/`dev-api` set true | `true` applies pending migrations at startup; `false`/absent only verifies the schema is current and fails with `Database schema is not up to date; run the migrate service` otherwise (use the migration service) |

Not required for a normal production runtime: `HOST_PROJECT_DIR`, `LOCAL_UID`, `LOCAL_GID`, `DEV_API_PORT`,
`APP_UID`, `APP_GID` (built-in default 10001:10001), `SIGNING_KEY_DIR`, and any PEM/PFX setting.

## Reference

| Environment key | Required/default/range | Development | Production and failure behavior |
|---|---|---|---|
| `Jwt__Issuer` | Required, non-empty | Required | Required; startup fails if empty |
| `Jwt__Audience` | Required, non-empty | Required | Required; startup fails if empty |
| `Jwt__AccessTokenMinutes` | Default 15; 1–60 | Same | Out of range fails startup |
| `Jwt__Signing__PrivateKeyPemPath` | Optional external source | Optional; if no source is configured a throwaway key is created under `/data/dev-keys` | External PEM (RSA >= 2048 bits). Selecting it disables the generated key. With PFX also set, startup fails |
| `Jwt__Signing__CertificatePath` | Optional external source | Alternative to PEM | External PFX; requires the password. Selecting it disables the generated key |
| `Jwt__Signing__CertificatePassword` | Required with PFX | External configuration | Required with `CertificatePath`; a password without a path fails startup; missing/wrong password fails startup; never commit it |
| `Jwt__Signing__AutoKeyPath` | Optional | Not used (Development uses `/data/dev-keys`) | Location of the generated key. Default `<directory of Database__Path>/signing/signing.pem`, i.e. `/data/signing/signing.pem` in Production |
| `Jwt__Signing__AllowKeyRegeneration` | Default `false` | Not used | Recovery opt-in; see "Automatic signing key" |
| `Jwt__Signing__PreviousPublicKeyPaths__N` | Optional array | Public PEM/certificate paths | Published for planned overlap; omit compromised keys during emergency replacement |
| `Session__RefreshTokenDays` | Default 7; 1–90 | Same | Out of range fails startup |
| `Session__SessionMaxDays` | Default 30; 1–365 | Same | Must be >= `RefreshTokenDays`; otherwise startup fails. This is the authoritative FR-016 bound |
| `Identity__ResetCodeHours` | Default 24; 1–72 | Same | Out of range fails startup |
| `Identity__LockoutMaxFailedAttempts` | Default 5; >=1 | Same | Out of range fails startup |
| `Identity__LockoutMinutes` | Default 15; >=1 | Same | Out of range fails startup |
| `Bootstrap__AdminEmail` | Required only while the user store is empty | Empty store may start without it; bootstrap is skipped | Empty store: a valid email is required, otherwise startup fails. Ignored (not even validated) once any user exists |
| `Bootstrap__AdminPassword` | Required only while the user store is empty; password policy applies | Treat as secret | Empty store: required and policy-compliant, otherwise startup fails (the value is never logged). Ignored once any user exists; removing it afterwards is optional hardening |
| `Database__Path` | Default `/data/auth.db`, non-empty | Dev volume | Production volume; also determines the default generated-key location |
| `Database__DataProtectionKeysPath` | Default `/data/keys`, non-empty | Dev volume | Persist and back up with DB so reset-code protection survives replacement |
| `Database__MigrateOnStartup` | Absent = `false`; `.env.production.example` sets `true` | `dev`/`dev-api` set true | `true` applies pending migrations at startup; `false`/absent only verifies the schema is current and fails with `Database schema is not up to date; run the migrate service` otherwise (use the migration service) |
| `Cors__AllowedOrigins__N` | Production: at least one required | Empty becomes `http://localhost:4200` | See "Production CORS"; invalid or missing fails startup |
| `RateLimit__PermitLimit` | Default 10; 1–1000 | Per remote source/window | Out of range fails startup |
| `RateLimit__WindowMinutes` | Default 1; 1–60 | Same | Out of range fails startup |
| `ForwardedHeaders__Enabled` | Default false | Optional | Enable behind a trusted reverse proxy for forwarded address/scheme handling |
| `HOST_PROJECT_DIR` | Development only | Host-visible absolute checkout path for `compose.dev.yaml` | Not used by the production `compose.yaml` |
| `LOCAL_UID`, `LOCAL_GID` | Development only; defaults 1000:1000 | Development containers only | Never used by production |
| `APP_UID`, `APP_GID` | Optional override; default 10001:10001 | Not used by dev containers | Runtime/migration identity; the default needs no configuration |
| `SIGNING_KEY_DIR` | Only for external PEM/PFX | Local validation convenience | Absolute host directory mounted read-only at `/run/secrets/signing`; unset, an empty named volume is mounted and no host path is involved. There is no implicit default |
| `API_PORT`, `DEV_API_PORT` | 8080/8081 | Human host access | Agent checks use Compose DNS, not these ports |

`.env` is not versioned and should be mode 0600 where supported. Environment variables may be visible via
container inspection. Production should prefer mounted key-per-file secrets under `/run/secrets/config`
(for example `Jwt__Signing__CertificatePassword` and `Bootstrap__AdminPassword`) or the deployment
platform's secret mechanism.

## Signing source selection (Production)

1. `Jwt__Signing__PrivateKeyPemPath` set -> that PEM is the signing key.
2. `Jwt__Signing__CertificatePath` set (with its password) -> that PFX is the signing key.
3. Neither set -> the automatically generated key is used.

Rules: both PEM and PFX fail startup; PFX path without password, or a password without a path, fails; an explicit
file that is missing, unreadable, invalid or smaller than 2048 bits fails. An explicit source always wins and
never falls back to the generated key; the generated-key location is not created or read.

## Automatic signing key

With no explicit source, the first start (empty database) generates an RSA 3072 key, serialised as a PKCS#8 PEM
(`BEGIN PRIVATE KEY`), at `Jwt__Signing__AutoKeyPath` (default `/data/signing/signing.pem`). The directory is
mode 0700 and the file 0600, written through a temporary file that is moved into place without overwriting.
It is reused on every later start, so the JWKS `kid` is stable while the volume is intact. The key is never
logged and never exposed by any endpoint.

| State at startup | Result |
|---|---|
| No key, no users | Generate and persist |
| Valid key | Reuse |
| No key, users exist | Startup fails (`Persisted signing key is missing but users exist ...`) |
| No key, users exist, `AllowKeyRegeneration=true` | Replacement generated: new `kid`, warning logged |
| Key present but unreadable/malformed/not RSA/< 2048 bits | Startup fails, file untouched, even with `AllowKeyRegeneration=true` |

`Jwt__Signing__AllowKeyRegeneration` defaults to `false`. It is an exceptional, explicit recovery switch: it
only acts when the file is absent, never overwrites or repairs a file, logs a warning on every start while it is
enabled, and a replacement invalidates all access tokens signed by the lost key. Remove it after recovery.

## Production CORS

`Cors__AllowedOrigins__N` must contain at least one entry. Each entry must be an exact absolute `http` or
`https` origin: scheme, host and optional port. Startup fails for `*` (anywhere, including `https://*.example.com`),
user info, a path, a trailing slash, a query, a fragment, any other scheme, or an empty list. Explicit
`localhost` origins are allowed. Values are matched exactly as written; nothing is normalised (a trailing slash is
rejected, not removed). Development keeps its `http://localhost:4200` fallback.

## Bootstrap

The bootstrap pair is needed only while the user store is empty: both values, a valid email, and a password that
meets the password policy; otherwise Production startup fails with a message that never contains the password.
As soon as any user exists the values are ignored entirely: stale, partial, invalid or changed values neither
fail startup nor create another account.

## Compatibility changes to be aware of

- `SIGNING_KEY_DIR` no longer defaults to `${HOST_PROJECT_DIR}/secrets`; set it explicitly for external PEM/PFX.
- Production now requires at least one valid CORS origin (an empty list used to start with no cross-origin access).
- Production may now set `Database__MigrateOnStartup=true` (it used to be rejected); absent still means `false`.
- Compose is split: `compose.yaml` is production-only (`auth-api`, profile-gated `migrate`); the development
  services live in `compose.dev.yaml`, loaded through `COMPOSE_FILE` in the development `.env.example`.
- `APP_UID`/`APP_GID` now default to 10001 instead of being required.
