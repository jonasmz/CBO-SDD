# Production deployment

## Quick deployment

For a small installation on a single host, three commands are enough:

```bash
cp .env.production.example .env
edit .env
docker compose up -d --build
```

`edit .env` means: open the copied file in your editor and fill in the values below. Keep the file private
(mode 0600 where supported) and out of version control; it contains the first Administrator's password.

| Value | Required | Meaning |
|---|---|---|
| `Bootstrap__AdminEmail` | yes | Email of the first Administrator |
| `Bootstrap__AdminPassword` | yes | Password of the first Administrator; must satisfy the password policy (at least 8 characters with upper-case, lower-case and a digit) |
| `Jwt__Issuer` | yes | Issuer written into access tokens; consumer APIs validate it |
| `Jwt__Audience` | yes | Audience written into access tokens; consumer APIs validate it |
| `Cors__AllowedOrigins__0` | yes | The browser origin of your front end, for example `https://app.example.com` (scheme + host, optional port, no trailing slash, no path) |
| `API_PORT` | no | Host port for the API (default `8080`) |

The template already sets `Database__MigrateOnStartup=true`; leave it as it is.

What the first start does by itself:

1. Creates the named volume `authservice-data` and keeps the SQLite database, the ASP.NET Core Data Protection
   keys and the signing key in it, so everything survives restarts, rebuilds and container recreation.
2. Applies the database migrations (because of `Database__MigrateOnStartup=true`) and creates the three fixed
   roles.
3. Generates an RSA signing key and stores it in the volume, because no external key is configured. Its public
   part is published at `/.well-known/jwks.json`; the key is generated once and reused afterwards.
4. Creates the first Administrator from the two `Bootstrap__*` values. This happens only while the database has
   no users; later changes to these values never create another account.
5. Starts serving requests. If any of the steps above fails, the container exits and nothing is served; see
   `docker compose logs auth-api`.

Check it and sign in (replace `8080` if you changed `API_PORT`):

```bash
docker compose ps                                   # auth-api becomes "healthy"
curl -fsS -X POST http://localhost:8080/api/auth/login \
  -H 'content-type: application/json' \
  -d '{"email":"<Bootstrap__AdminEmail>","password":"<Bootstrap__AdminPassword>"}'
```

The service runs as a non-root user and listens on plain HTTP. Direct use on a local machine or a trusted LAN
is fine. Anything reachable from the Internet should sit behind a TLS-terminating reverse proxy (see
[Advanced deployment](#advanced-deployment)).

Keep in mind: **deleting the `authservice-data` volume deletes the users, sessions and the signing key** of this
installation. A new signing key means every previously issued access token stops validating and consumers must
fetch the new JWKS. See [operations.md](operations.md) for backup and recovery.

Everything below this line is optional.

## Advanced deployment

None of this is needed for the quick deployment above. Use it when you need tighter control.

### External signing key (PEM or PFX)

If you configure a signing source yourself, it is used and no key is generated. Configure **exactly one**:
an RSA private key in PEM form (at least 2048 bits), or a PFX plus its password. Configuring both, a PFX path
without a password, a password without a path, or a file that is missing or invalid makes startup fail; the
service never falls back to the generated key.

Mount the directory holding the key read-only by setting `SIGNING_KEY_DIR` to an absolute host path outside
the checkout (it is mounted at `/run/secrets/signing`). With a PEM:

```bash
openssl genrsa -out /srv/auth-service/signing/current.pem 3072     # or bring your own key
chgrp 10001 /srv/auth-service/signing/current.pem && chmod 0640 /srv/auth-service/signing/current.pem
```

```dotenv
SIGNING_KEY_DIR=/srv/auth-service/signing
Jwt__Signing__PrivateKeyPemPath=/run/secrets/signing/current.pem
```

The container user must be able to read the file (group `10001` above, or your custom `APP_GID`). With a PFX:

```dotenv
SIGNING_KEY_DIR=/srv/auth-service/signing
Jwt__Signing__CertificatePath=/run/secrets/signing/current.pfx
Jwt__Signing__CertificatePassword=...
```

Certificate expiry is logged but not enforced; monitor and rotate before it expires. Only JWKS or public
key material goes to consumers.

### Secret mounts (PFX password, bootstrap password)

Environment variables can be visible through container inspection. The service also reads ASP.NET Core
key-per-file secrets from `/run/secrets/config`: a file named `Jwt__Signing__CertificatePassword` (or
`Bootstrap__AdminPassword`) containing only the value. Compose does not mount that directory by default; add it
with an extra Compose file and list it in `.env`:

```yaml
# compose.secrets.yaml
services:
  auth-api:
    volumes:
      - /srv/auth-service/config:/run/secrets/config:ro
```

```dotenv
COMPOSE_FILE=compose.yaml:compose.secrets.yaml
```

Values from `/run/secrets/config` take precedence over environment variables, so leave the corresponding
`.env` entries empty.

### Explicit (manual) migrations

To control when the schema changes, disable automatic migrations and run the migration service yourself:

```dotenv
Database__MigrateOnStartup=false
```

```bash
docker compose build auth-api migrate
docker compose run --rm migrate          # applies pending migrations, seeds the fixed roles, exits
docker compose up -d auth-api
```

`migrate` is profile-gated, so `docker compose up` never starts it (it would otherwise race an
auto-migrating API on the same SQLite file); `docker compose run --rm migrate` still runs it explicitly. It
needs no JWT, signing or bootstrap settings. With `Database__MigrateOnStartup=false`, or when the key is
absent (the default when it is not set), the API never migrates by itself: it checks that the schema is current
and otherwise exits with `Database schema is not up to date; run the migrate service`. Existing installations
that never set the key therefore keep working exactly as before. See [database.md](database.md).

### Custom runtime user (`APP_UID` / `APP_GID`)

The runtime is non-root with a built-in default of `10001:10001`. To use other numeric IDs set them in `.env`
and rebuild:

```dotenv
APP_UID=20001
APP_GID=20001
```

```bash
docker compose up -d --build
```

A new volume inherits the ownership. For an existing volume change its owner once, for example
`docker run --rm -v authservice-data:/data alpine chown -R 20001:20001 /data`. The generated signing key stays
mode 0600 (directory 0700) and is owned by the runtime user. These IDs are unrelated to the development-only
`LOCAL_UID`/`LOCAL_GID`.

### Recovering from a lost generated signing key

If users exist but `/data/signing/signing.pem` is missing (for example after restoring only the database),
startup fails with `Persisted signing key is missing but users exist`. Restoring the key from backup is the
right fix. If it is truly lost, you can deliberately replace it:

```dotenv
Jwt__Signing__AllowKeyRegeneration=true
```

**This is a key-replacement event.** A new key and a new `kid` are generated, every access token signed by the
old key stops validating, and consumers must refresh their JWKS. The flag only acts when the file is absent; it
never overwrites an existing key and never repairs a corrupt one (that still fails startup). A warning is logged
on every start while it is set, so remove it after recovery and recreate the container.

### Key rotation

Planned rotation uses an external key: configure the new key as the current PEM/PFX and publish the old public
key during the overlap with `Jwt__Signing__PreviousPublicKeyPaths__0=/run/secrets/signing/old-public.pem`. To
move off the generated key, export its public part first
(`openssl pkey -in signing.pem -pubout -out old-public.pem`). Wait at least one access-token lifetime plus the
one-minute skew, then remove the old public key. Emergency replacement drops the compromised key
immediately; see [operations.md](operations.md) and [jwt-consumer-integration.md](jwt-consumer-integration.md).

### Reverse proxy and TLS

The API speaks plain HTTP on port 8080 inside the container. For Internet-facing use, terminate TLS in a
reverse proxy you operate and forward to the published port. Set `ForwardedHeaders__Enabled=true` only when
the proxy is trusted and overwrites `X-Forwarded-For` and `X-Forwarded-Proto`; the rate limiter and logs then
see the client address. This project does not ship a proxy or manage certificates.

### Other operational notes

- Single instance only: never run two API writers against the same SQLite volume.
- `/openapi/v1.json` and `/scalar` are anonymously readable in Production (request execution is disabled);
  restrict them at the proxy if your policy requires it. `/health` returns only `Healthy`/`Unhealthy`.
- The generated key, database and Data Protection keys are all in `authservice-data`; see
  [database.md](database.md) for the layout and [operations.md](operations.md) for backups.
