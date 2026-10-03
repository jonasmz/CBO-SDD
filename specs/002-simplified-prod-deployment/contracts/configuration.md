# Configuration & Startup Contract

## Operator-facing (`.env.production.example`)

| Key | Required | Default |
|---|---|---|
| `Bootstrap__AdminEmail` | yes (empty DB) | — |
| `Bootstrap__AdminPassword` | yes (empty DB) | — |
| `Jwt__Issuer` | yes | — |
| `Jwt__Audience` | yes | — |
| `Cors__AllowedOrigins__0` | yes | — |
| `API_PORT` | no | 8080 |
| `Database__MigrateOnStartup` | pre-set `true` in template | app/compose default `false` |

Not required: `HOST_PROJECT_DIR`, `LOCAL_UID`, `LOCAL_GID`, `DEV_API_PORT`, `APP_UID`, `APP_GID` (default 10001), `SIGNING_KEY_DIR`, PEM/PFX settings.

## Advanced keys
`Jwt__Signing__PrivateKeyPemPath`, `Jwt__Signing__CertificatePath`, `Jwt__Signing__CertificatePassword`, `Jwt__Signing__PreviousPublicKeyPaths__N`, `Jwt__Signing__AllowKeyRegeneration` (recovery only), `Jwt__Signing__AutoKeyPath` (default `<Database.Path dir>/signing/signing.pem`), `SIGNING_KEY_DIR`, `APP_UID`/`APP_GID`, `Database__MigrateOnStartup=false`.

## Startup failure contract (exit non-zero, clear secret-free message, nothing served)
Missing issuer/audience; Production CORS empty/`*`/trailing slash/path/non-http(s); signing-source conflicts or invalid material; users exist + auto key absent (no recovery flag); corrupt auto key; schema outdated when migration disabled; migration failure; empty DB with missing/partial/invalid/weak bootstrap credentials.

## Production CORS rules (`Cors__AllowedOrigins__N`)
At least one entry. Each entry: absolute `http`/`https` origin = scheme + host (required) + optional port. Rejected: `*` anywhere (including wildcard hosts), userinfo (`@`), path, trailing slash, query, fragment, other schemes. Explicit localhost origins are allowed. No normalization; matching is exact. Development keeps its localhost fallback.

## Unchanged public HTTP contracts
All endpoints, JWKS, OpenAPI/Scalar.
