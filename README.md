# Central Authentication and Authorization Service

This service owns email/password credentials, issues RS256 access tokens and rotating refresh tokens,
publishes public verification keys, and administers the fixed `Administrator`, `Manager`, and `Operator`
roles. It is a .NET 10 API using ASP.NET Core Identity, EF Core, and SQLite.

The code follows four inward-pointing layers: Domain, Application, Infrastructure, and API. Development,
builds, tests, migrations, and runtime operations are Docker-first; a local .NET installation is neither
needed nor supported by the documented workflow.

## Prerequisites and quick start

- Docker Engine with Compose v2
- permission to use the Docker daemon
- network access for the initial image build and NuGet restore
- writable Docker named volumes

```bash
cp .env.example .env
# Set HOST_PROJECT_DIR to the absolute checkout path as seen by the Docker daemon.
# Set bootstrap credentials, APP_UID/APP_GID, and production signing configuration.
chmod 0600 .env
docker compose build dev tools
docker compose run --rm dev dotnet restore
docker compose run --rm dev dotnet build --no-restore
docker compose run --rm dev dotnet test --no-build
docker compose --profile run up -d dev-api
docker compose run --rm -T tools curl -fsS http://dev-api:8080/health
```

Create migrations in the development container and apply them with the one-shot migration mode:

```bash
docker compose run --rm dev dotnet ef migrations add <Name> \
  --project src/AuthService.Infrastructure --startup-project src/AuthService.Api \
  --output-dir Persistence/Migrations
docker compose run --rm migrate
```

Login is `POST /api/auth/login`; access tokens are short-lived JWTs and refresh tokens are opaque,
single-use session credentials. See the authentication and consumer guides before integrating.

## Repository map

- `src/AuthService.Domain` — framework-independent roles and security/session rules
- `src/AuthService.Application` — use cases and narrow infrastructure ports
- `src/AuthService.Infrastructure` — Identity, EF Core/SQLite, sessions, and JWT signing
- `src/AuthService.Api` — HTTP endpoints, middleware, policies, OpenAPI, and composition
- `tests/` — minimal integration and architecture verification
- `compose.yaml`, `Dockerfile*` — development tooling and production runtime images
- `docs/` — detailed development, deployment, integration, and operations guides

## Documentation

- [Architecture](docs/architecture.md)
- [Development](docs/development.md)
- [Authentication](docs/authentication.md)
- [Authorization](docs/authorization.md)
- [JWT consumer integration](docs/jwt-consumer-integration.md)
- [Configuration reference](docs/configuration.md)
- [Database, backup, and restore](docs/database.md)
- [Deployment](docs/deployment.md)
- [Operations](docs/operations.md)

The HTTP and token contracts are in `specs/001-central-auth-service/contracts/`. Production should be
placed behind a TLS-terminating reverse proxy and must never be exposed directly to untrusted networks.
