# Docker-first development

Only Docker Engine and Compose v2 are required. The initial image build and restore need network access;
the Docker daemon must be usable and the named cache/data volumes must be writable.

## Environment and identities

```bash
cp .env.example .env
chmod 0600 .env
```

Set `HOST_PROJECT_DIR` to the absolute checkout path as seen by the host Docker daemon. On a host this is
usually `pwd`. If the developer/agent itself runs in a container, inspect that container's mounts from the
host (`docker inspect <container>`) and use the host-side `Source` corresponding to this checkout. A path
visible only inside the agent container will not work for the bind mount.

`LOCAL_UID`/`LOCAL_GID` identify only `dev`, `dev-api`, and local validation helpers (defaults 1000:1000).
`APP_UID`/`APP_GID` identify only the production runtime and migration containers (documented defaults
10001:10001). The sets are independent and must not be mixed; `.env` is authoritative for Compose.

## Build, restore, build, and test

```bash
docker compose config
docker compose build dev tools
docker compose run --rm dev dotnet restore
docker compose run --rm dev dotnet build --no-restore
docker compose run --rm dev dotnet test --no-build
```

NuGet packages are cached in `nuget-cache`. Development SQLite and Data Protection state live in
`authservice-dev-data`.

## Migrations

Create a migration after changing the EF model:

```bash
docker compose run --rm dev dotnet ef migrations add <Name> \
  --project src/AuthService.Infrastructure \
  --startup-project src/AuthService.Api \
  --output-dir Persistence/Migrations
```

Apply migrations to the development volume with the development API, or explicitly:

```bash
docker compose run --rm dev dotnet run --project src/AuthService.Api -- --migrate
```

Production migrations use `docker compose run --rm migrate`; see the database guide.

## Run and inspect Development

Configure `.env`, then:

```bash
docker compose --profile run up -d dev-api
docker compose run --rm -T tools sh -c \
  'until curl -fsS http://dev-api:8080/health; do sleep 2; done'
docker compose run --rm -T tools curl -fsS http://dev-api:8080/openapi/v1.json
docker compose run --rm -T -v authservice-dev-data:/data:ro tools \
  sqlite3 /data/auth.db 'select "Name" from "AspNetRoles" order by "Name";'
```

Agent-container checks use service names on the Compose network. Do not use host-published `localhost`
ports from an agent container; those ports are for humans on the Docker host.

## Stop and recreate

```bash
docker compose --profile run down             # keep named data/cache volumes
docker compose --profile run down -v          # delete all Compose named volumes
docker compose --profile run up -d dev-api    # recreate containers
```

Deleting `authservice-dev-data` deletes development users, sessions, and Data Protection keys. Do not use
`down -v` against production data unless destruction is explicitly intended and a verified backup exists.
