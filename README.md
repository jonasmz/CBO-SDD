# Central Authentication and Authorization Service

This service owns email/password credentials, issues RS256 access tokens and rotating refresh tokens,
publishes public verification keys, and administers the fixed `Administrator`, `Manager`, and `Operator`
roles. It is a .NET 10 API using ASP.NET Core Identity, EF Core, and SQLite.

The code follows four inward-pointing layers: Domain, Application, Infrastructure, and API. Development,
builds, tests, migrations, and runtime operations are Docker-first; a local .NET installation is neither
needed nor supported by the documented workflow.

## Production quick start

To run the service on a small installation:

```bash
cp .env.production.example .env
edit .env
docker compose up -d --build
```

Fill in the Administrator email/password, token issuer and audience, and your front end's CORS origin. The
first start creates the database, applies migrations, generates a persistent signing key and creates the first
Administrator. Full instructions, advanced options (external signing keys, manual migrations, recovery) and
backup guidance: [Production deployment](docs/deployment.md).

## Development quick start

Prerequisites: Docker Engine with Compose v2, permission to use the Docker daemon, network access for the
initial image build and NuGet restore, and writable Docker named volumes.

```bash
cp .env.example .env
# Set HOST_PROJECT_DIR to the absolute checkout path as seen by the Docker daemon.
# Set Jwt__Issuer and Jwt__Audience. .env.example already sets COMPOSE_FILE to load compose.dev.yaml.
chmod 0600 .env
docker compose build dev tools
docker compose run --rm dev dotnet restore
docker compose run --rm dev dotnet build --no-restore
docker compose run --rm dev dotnet test --no-build
docker compose --profile run up -d dev-api
docker compose run --rm -T tools curl -fsS http://dev-api:8080/health
```

Create migrations in the development container and apply them with the one-shot migration mode (this is also the
manual-migration path for production; the production template migrates automatically):

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
- `compose.yaml` (production runtime), `compose.dev.yaml` (development/tooling services), `Dockerfile*`
- `.env.production.example` (production settings) and `.env.example` (development settings)
- `docs/` — detailed development, deployment, integration, and operations guides

## Documentation

- [Architecture](docs/architecture.md)
- [Development](docs/development.md)
- [Authentication](docs/authentication.md)
- [Authorization](docs/authorization.md)
- [JWT consumer integration](docs/jwt-consumer-integration.md)
- [Configuration reference](docs/configuration.md)
- [Database, backup, and restore](docs/database.md)
- [Production deployment](docs/deployment.md)
- [Operations](docs/operations.md)

The HTTP and token contracts are in `specs/001-central-auth-service/contracts/`. Production should be
placed behind a TLS-terminating reverse proxy and must never be exposed directly to untrusted networks.
