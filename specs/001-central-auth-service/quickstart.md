# Quickstart: Validate the Central Authentication Service

All commands run on the host with Docker only. No .NET is installed locally and none is installed in
the agent container. When the agent itself runs in a container, every HTTP check and database
inspection goes through the repository-built `tools` container on the compose network (research R14);
host-published ports and agent-side `curl` are never used. Contracts:
[http-api.md](contracts/http-api.md), [token-and-jwks.md](contracts/token-and-jwks.md). Entities:
[data-model.md](data-model.md).

## Prerequisites

- Docker with Compose v2
- Repository checkout; `cp .env.example .env` and set `HOST_PROJECT_DIR` to the repository path **as seen
  by the host Docker daemon** (the same path when running on the host; when the agent runs in a
  container, see `docs/development.md` for how to find it), plus `Bootstrap__AdminEmail`,
  `Bootstrap__AdminPassword` (meets the password rule) and, for production-mode steps, `SIGNING_KEY_DIR`

For production-mode steps generate a throwaway validation key once (the same command as in `docs/deployment.md`; the file must be readable by the runtime user, so this is for local validation only):

```bash
set -a; . ./.env; set +a; mkdir -p secrets
docker compose run --rm -T --user "${LOCAL_UID:-1000}:${LOCAL_GID:-1000}" \
  -v "${SIGNING_KEY_DIR:-${HOST_PROJECT_DIR}/secrets}:/out" tools \
  sh -c 'openssl genrsa -out /out/signing.pem 2048 && chmod 0644 /out/signing.pem'
```

and set `Jwt__Signing__PrivateKeyPemPath=/run/secrets/signing/signing.pem` in `.env`.

Shorthand used below:

```bash
tools() { docker compose run --rm -T tools "$@"; }   # curl, sqlite3, openssl live here
```

## 1. Build and test in the dev container

```bash
docker compose build
docker compose run --rm dev dotnet restore
docker compose run --rm dev dotnet build --no-restore
docker compose run --rm dev dotnet test --no-build
```
Expected: build succeeds with zero security-related warnings; the test suite passes (validation gate
items 1 to 10 are covered by the integration tests, item 11 is this step).

## 2. Create and apply migrations

```bash
docker compose run --rm dev dotnet ef migrations add <Name> \
  --project src/AuthService.Infrastructure --startup-project src/AuthService.Api
docker compose run --rm migrate
```
Expected: migration files appear under `src/AuthService.Infrastructure/Persistence/Migrations`; the
`migrate` service applies migrations, seeds the three roles and exits 0.

## 3. Run the service

```bash
docker compose up -d auth-api
tools sh -c 'until curl -sf http://auth-api:8080/health; do sleep 2; done'   # Healthy
```
Expected: first start with an empty database creates the bootstrap Administrator (log line
`Bootstrap administrator created`). For Development runs use `docker compose --profile run up -d dev-api`
and `http://dev-api:8080`.

## 4. Smoke scenarios (replace `<base>` with `http://auth-api:8080`)

```bash
tools curl -s -X POST <base>/api/auth/login -H 'content-type: application/json' \
  -d '{"email":"<admin email>","password":"<admin password>"}'
tools curl -s <base>/api/me -H "authorization: Bearer <accessToken>"
# create a Manager, then confirm the Manager gets 403 on GET /api/users
# rotate: POST /api/auth/refresh with the refreshToken; reuse the old one -> 401 and session revoked
# logout: POST /api/auth/logout with the refreshToken; refresh again -> 401
# unsupported role: PUT /api/users/{id}/roles/Root -> 400
# time the login request: expect well under 2 seconds (SC-001)
```

## 5. Bootstrap is closed

Restart with the same configuration: logs show `Bootstrap skipped: users already exist` and no new user
is created. `GET /api/users` as the Administrator still lists the same users.

## 6. Persistence survives container replacement

```bash
docker compose down            # keeps the named volume
docker compose up -d auth-api
# sign in again with the same credentials: succeeds
```

## 7. Independent token validation

Fetch `<base>/.well-known/jwks.json`, configure a sample consumer (see
`docs/jwt-consumer-integration.md`) with only that public key, and confirm: valid token accepted;
expired, tampered, wrong-issuer and wrong-audience tokens rejected; Administrator-only endpoint rejects a
Manager and Operator token.

## 8. Backup and restore drill

Follow `docs/database.md`: stop `auth-api`, copy `/data/auth.db` and `/data/keys` with the `tools` image into a
backup, restore them into a throwaway volume, start a service instance on it and confirm the same users,
roles and enabled states exist and sign-in works (SC-012).

## 9. Documentation modes

- Development (`ASPNETCORE_ENVIRONMENT=Development`, service `dev-api`): `/scalar` renders and requests
  can be executed from it with a bearer token.
- Production (`ASPNETCORE_ENVIRONMENT=Production`, valid signing configuration): `/scalar` renders; the
  page has no send/execute control, no server URL and no credential input (SC-008). Starting Production
  with the signing key missing exits with a clear configuration error.
