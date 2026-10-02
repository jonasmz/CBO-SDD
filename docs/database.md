# Database, migrations, backup, and restore

SQLite is stored at `/data/auth.db`. Compose uses `authservice-dev-data` for Development and
`authservice-data` for Production. Data Protection keys default to `/data/keys`; back them up with the
database so outstanding Identity reset codes remain decryptable after container replacement.

The deployment is single-instance. Do not run two API writers against the same SQLite file/volume.
`EnsureCreated` is not used or supported; schema changes use EF Core migrations.

## Clean install and upgrade

```bash
docker compose run --rm migrate
```

This applies all pending migrations from zero and idempotently seeds the three fixed roles. Rerunning it is
safe. Normal Production startup never applies migrations; an absent/outdated schema fails with
`Database schema is not up to date; run the migrate service`.

Create a migration only in the development SDK container:

```bash
docker compose run --rm dev dotnet ef migrations add <Name> \
  --project src/AuthService.Infrastructure --startup-project src/AuthService.Api \
  --output-dir Persistence/Migrations
```

Before an upgrade, stop the API and take a verified backup. If migration fails or is partial, do not use
`EnsureCreated` or edit the database manually; stop services, preserve logs, restore the pre-upgrade backup,
correct the migration, and retry. Downgrades/destructive migrations require an explicit data migration and
restore plan; none are implied by the initial schema.

## Consistent backup

Set `HOST_PROJECT_DIR` and use only the repository-built tools image:

```bash
mkdir -p backup/auth-service
docker compose stop auth-api
docker compose run --rm -T \
  -v authservice-data:/data:ro \
  -v "${HOST_PROJECT_DIR}/backup/auth-service:/backup" \
  tools sh -c 'cp /data/auth.db /backup/auth.db && cp -a /data/keys /backup/keys'
docker compose start auth-api
```

Record checksums and protect the backup as credential-bearing data. Signing private keys are separate
custody: do not put them in the database backup; back them up/escrow through the operator's existing secure
secret process.

## Restore drill and success check

```bash
docker volume create authservice-restore-data
docker compose run --rm -T \
  -v authservice-restore-data:/restore \
  -v "${HOST_PROJECT_DIR}/backup/auth-service:/backup:ro" \
  tools sh -c 'cp /backup/auth.db /restore/auth.db && cp -a /backup/keys /restore/keys && chown -R 10001:10001 /restore'
docker compose run -d --name authservice-restore-check --no-deps \
  -e Database__Path=/restore/auth.db \
  -e Database__DataProtectionKeysPath=/restore/keys \
  -v authservice-restore-data:/restore auth-api
docker compose run --rm -T tools sh -c \
  'until curl -fsS http://authservice-restore-check:8080/health; do sleep 2; done'
```

Use the configured `APP_UID:APP_GID` instead of `10001:10001` if deployment values differ. Restore succeeds
only when user count, role memberships, enabled states, and login match the source and health is `Healthy`.
Then clean up the drill:

```bash
docker stop authservice-restore-check
docker rm authservice-restore-check
docker volume rm authservice-restore-data
```
