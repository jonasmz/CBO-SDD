# Database, migrations, backup, and restore

SQLite is stored at `/data/auth.db`. Compose uses `authservice-dev-data` for Development and
`authservice-data` for Production. The deployment is single-instance: do not run two API writers against the
same SQLite file/volume. `EnsureCreated` is not used or supported; schema changes use EF Core migrations.

## Persistent layout

Everything an installation needs is inside the one production volume `authservice-data`, mounted at `/data`:

| Path | Content | Permissions |
|---|---|---|
| `/data/auth.db` | SQLite database (users, roles, refresh sessions) | runtime user |
| `/data/keys` | ASP.NET Core Data Protection key ring (keeps reset codes decryptable) | runtime user |
| `/data/signing/signing.pem` | Automatically generated RSA signing key (PKCS#8 PEM) | directory 0700, file 0600, runtime user |

`/data/signing` exists only when automatic signing is in use; with an external PEM/PFX it is never created.
**Deleting the production volume deletes all three: the database, the Data Protection keys and the generated
signing identity.** The next start behaves like a fresh installation with a *new* signing key (a key-replacement
event: previously issued access tokens stop validating and consumers must refresh the JWKS).

## Migration modes

Both modes use the same `DatabaseInitializer`; they differ only in whether the schema is changed.

**Simple mode - `Database__MigrateOnStartup=true`** (set by `.env.production.example`). At startup the API applies
pending migrations, seeds the three fixed roles, then continues (signing key, bootstrap Administrator) and only
then serves requests.

**Manual/advanced mode - `Database__MigrateOnStartup=false` or absent** (the code default). The API never
migrates by itself: it verifies the schema is current and otherwise stops with

```text
Database schema is not up to date; run the migrate service
```

Run the migration explicitly before starting or upgrading the API:

```bash
docker compose run --rm migrate
```

It applies all pending migrations from zero, seeds missing fixed roles, and exits; rerunning is safe. `migrate` is
profile-gated, so `docker compose up` never starts it, but `docker compose run --rm migrate` runs it. It needs no
JWT, signing, bootstrap or CORS settings.

In either mode, any initialization failure (migration error, outdated schema, missing/corrupt signing key,
invalid bootstrap configuration) terminates the process before the server starts, so no request is ever served
half-initialized. Check `docker compose logs auth-api`.

Create a migration only in the development SDK container (see [development.md](development.md)):

```bash
docker compose run --rm dev dotnet ef migrations add <Name> \
  --project src/AuthService.Infrastructure --startup-project src/AuthService.Api \
  --output-dir Persistence/Migrations
```

Before an upgrade, stop the API and take a verified backup. If a migration fails or is partial, do not use
`EnsureCreated` or edit the database manually; stop services, preserve logs, restore the pre-upgrade backup,
correct the migration, and retry. Downgrades/destructive migrations require an explicit data migration and
restore plan; none are implied by the initial schema.

## Consistent backup

A complete backup of an installation that uses the automatically generated signing key is the database, the
Data Protection key ring **and** the signing key, taken together from the same stopped volume. Use any plain
Docker image; no repository tooling is needed:

```bash
mkdir -p backup/auth-service && chmod 0700 backup/auth-service
docker compose stop auth-api
docker run --rm -v authservice-data:/data:ro -v "$PWD/backup/auth-service:/backup" alpine \
  sh -c 'cp -a /data/auth.db /backup/auth.db && cp -a /data/keys /backup/keys && { [ ! -d /data/signing ] || cp -a /data/signing /backup/signing; }'
docker compose start auth-api
```

Record checksums and protect the backup as credential-bearing data: `signing.pem` is a private key. With an
external PEM/PFX there is no `/data/signing`; those keys stay under their own custody and secure process and
are not part of this backup.

## Restore drill and success check

Restore the three items into a scratch volume and start a throwaway API on it; the check never touches the
real volume's contents:

```bash
docker volume create authservice-restore-data
docker run --rm -v authservice-restore-data:/restore -v "$PWD/backup/auth-service:/backup:ro" alpine \
  sh -c 'cp -a /backup/. /restore/ && chown -R 10001:10001 /restore'
docker compose run -d --name authservice-restore-check --no-deps \
  -e Database__Path=/restore/auth.db \
  -e Database__DataProtectionKeysPath=/restore/keys \
  -v authservice-restore-data:/restore auth-api
docker exec authservice-restore-check dotnet AuthService.Api.dll --healthcheck && echo healthy
```

Use your `APP_UID:APP_GID` instead of `10001:10001` if you changed them. The generated key is found at
`/restore/signing/signing.pem` because it sits next to the restored database. Restore succeeds only when user
count, role memberships, enabled states and login match the source, the JWKS `kid` is unchanged and health is
`healthy`. Then clean up:

```bash
docker stop authservice-restore-check
docker rm authservice-restore-check
docker volume rm authservice-restore-data
```

Restoring only some of the three items is unsupported; the consequences are described in
[operations.md](operations.md).
