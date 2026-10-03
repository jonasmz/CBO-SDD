# Docker-first development

Only Docker Engine and Compose v2 are required. The initial image build and restore need network access;
the Docker daemon must be usable and the named cache/data volumes must be writable.

## Compose files and environment

Compose is split in two files:

- `compose.yaml` - the production runtime only (`auth-api` and the profile-gated `migrate`). It needs no
  development variables.
- `compose.dev.yaml` - the development and tooling services (`dev`, `dev-api`, `tools`) and their volumes.

`.env.example` sets `COMPOSE_FILE=compose.yaml:compose.dev.yaml`, so after

```bash
cp .env.example .env
chmod 0600 .env
```

every `docker compose ...` command in this guide sees both files and works without extra flags. Without that
variable (for example in a shell that ignores `.env`), pass the files explicitly:

```bash
docker compose -f compose.yaml -f compose.dev.yaml build dev tools
```

A production host copies `.env.production.example` instead and never sets `COMPOSE_FILE`; see
[deployment.md](deployment.md).

Set `HOST_PROJECT_DIR` to the absolute checkout path as seen by the host Docker daemon. On a host this is
usually `pwd`. If the developer/agent itself runs in a container, inspect that container's mounts from the
host (`docker inspect <container>`) and use the host-side `Source` corresponding to this checkout. A path
visible only inside the agent container will not work for the bind mount. Set `Jwt__Issuer` and
`Jwt__Audience` for `dev-api`.

`LOCAL_UID`/`LOCAL_GID` identify only `dev`, `dev-api`, and local validation helpers (defaults 1000:1000).
`APP_UID`/`APP_GID` are an optional override of the production runtime identity (built-in default 10001:10001)
and are independent of the development identity; do not mix them.

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

Manual production migrations use `docker compose run --rm migrate` (the production template migrates
automatically); see the database guide.

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
