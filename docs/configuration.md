# Configuration reference

ASP.NET Core accepts colon-separated keys; environment variables use double underscores. Invalid validated
values fail startup. Values below are the implemented defaults and ranges.

| Environment key | Required/default/range | Development | Production and failure behavior |
|---|---|---|---|
| `Jwt__Issuer` | Required, non-empty | Required | Required; startup fails if empty |
| `Jwt__Audience` | Required, non-empty | Required | Required; startup fails if empty |
| `Jwt__AccessTokenMinutes` | Default 15; 1–60 | Same | Out of range fails startup |
| `Jwt__Signing__PrivateKeyPemPath` | One signing source | Optional; if both sources absent, a throwaway key is created under `/data/dev-keys` | Exactly one of PEM/PFX required; missing, unreadable, invalid, or RSA <2048 bits fails startup |
| `Jwt__Signing__CertificatePath` | One signing source | Alternative to PEM | Alternative to PEM; exactly one source |
| `Jwt__Signing__CertificatePassword` | Required with PFX | External configuration | Missing/wrong password fails startup; never commit it |
| `Jwt__Signing__PreviousPublicKeyPaths__N` | Optional array | Public PEM/certificate paths | Published for planned overlap; omit compromised keys during emergency replacement |
| `Session__RefreshTokenDays` | Default 7; 1–90 | Same | Out of range fails startup |
| `Session__SessionMaxDays` | Default 30; 1–365 | Same | Must be >= `RefreshTokenDays`; otherwise startup fails. This is the authoritative FR-016 bound |
| `Identity__ResetCodeHours` | Default 24; 1–72 | Same | Out of range fails startup |
| `Identity__LockoutMaxFailedAttempts` | Default 5; >=1 | Same | Out of range fails startup |
| `Identity__LockoutMinutes` | Default 15; >=1 | Same | Out of range fails startup |
| `Bootstrap__AdminEmail` | Paired with password | Empty store may start without it; bootstrap is skipped | Empty store requires valid pair; startup fails if missing/invalid |
| `Bootstrap__AdminPassword` | Paired; password policy applies | Treat as secret | Empty-store Production startup fails if absent/invalid; remove after bootstrap |
| `Database__Path` | Default `/data/auth.db`, non-empty | Dev volume | Production volume; missing schema fails normal startup |
| `Database__DataProtectionKeysPath` | Default `/data/keys`, non-empty | Dev volume | Persist and back up with DB so reset-code protection survives replacement |
| `Database__MigrateOnStartup` | Default false | `dev`/`dev-api` set true | Must be false; true fails startup. Use migration service |
| `Cors__AllowedOrigins__N` | Default empty; absolute URIs | Empty becomes `http://localhost:4200` | Empty permits no cross-origin access; malformed URI fails startup |
| `RateLimit__PermitLimit` | Default 10; 1–1000 | Per remote source/window | Out of range fails startup |
| `RateLimit__WindowMinutes` | Default 1; 1–60 | Same | Out of range fails startup |
| `ForwardedHeaders__Enabled` | Default false | Optional | Enable behind a trusted reverse proxy for forwarded address/scheme handling |
| `HOST_PROJECT_DIR` | Required by Compose | Host-visible absolute checkout path | Also used for build/local paths |
| `LOCAL_UID`, `LOCAL_GID` | Defaults 1000:1000 | Development containers only | Never use as runtime identity |
| `APP_UID`, `APP_GID` | Defaults 10001:10001 in `.env.example` | Not used by dev containers | Required by Compose; authoritative runtime/migration identity |
| `SIGNING_KEY_DIR` | Host directory | Local validation convenience | Read-only signing mount; production path should be outside checkout |
| `API_PORT`, `DEV_API_PORT` | 8080/8081 | Human host access | Agent checks use Compose DNS, not these ports |

`.env` is not versioned and should be mode 0600 where supported. Environment variables may be visible via
container inspection. Production should prefer mounted key-per-file secrets under `/run/secrets/config`
(for example `Jwt__Signing__CertificatePassword` and `Bootstrap__AdminPassword`) or the deployment
platform's secret mechanism.
