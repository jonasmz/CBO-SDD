# Quickstart: Validate the Central Authentication Service

All commands run on the host with Docker only. No .NET is installed locally and none is installed in
the agent container. Contracts: [http-api.md](contracts/http-api.md),
[token-and-jwks.md](contracts/token-and-jwks.md). Entities: [data-model.md](data-model.md).

## Prerequisites

- Docker with Compose v2
- Repository checkout; `cp .env.example .env` and set `Bootstrap__AdminEmail`,
  `Bootstrap__AdminPassword` (meets the password rule) and the signing key location (development can
  auto-generate a key)

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
`migrate` service applies them to the volume database and exits 0.

## 3. Run the service

```bash
docker compose up -d auth-api
curl -s localhost:8080/health          # Healthy
```
Expected: first start with an empty database creates the bootstrap Administrator (log line
`Bootstrap administrator created`).

## 4. Smoke scenarios

```bash
# sign in as the bootstrap administrator
curl -s -X POST localhost:8080/api/auth/login -H 'content-type: application/json' \
  -d '{"email":"<admin email>","password":"<admin password>"}'
# use the accessToken
curl -s localhost:8080/api/me -H "authorization: Bearer <accessToken>"
# create a Manager, then confirm the Manager gets 403 on GET /api/users
# rotate: POST /api/auth/refresh with the refreshToken; reuse the old one -> 401 and session revoked
# logout: POST /api/auth/logout with the refreshToken; refresh again -> 401
# unsupported role: PUT /api/users/{id}/roles/Root -> 400
```

## 5. Bootstrap is closed

Restart with the same configuration: logs show `Bootstrap skipped: users already exist` and no new user
is created. `GET /api/users` as the Administrator still lists the same users.

## 6. Persistence survives container replacement

```bash
docker compose down            # keeps the volume
docker compose up -d auth-api
# sign in again with the same credentials: succeeds
```

## 7. Independent token validation

Fetch `localhost:8080/.well-known/jwks.json`, configure a sample consumer (see
`docs/jwt-consumer-integration.md`) with only that public key, and confirm: valid token accepted;
expired, tampered, wrong-issuer and wrong-audience tokens rejected; Administrator-only endpoint rejects a
Manager and Operator token.

## 8. Documentation modes

- Development (`ASPNETCORE_ENVIRONMENT=Development`): open `/scalar`, sign in, execute a request from the
  page.
- Production (`ASPNETCORE_ENVIRONMENT=Production`, valid signing configuration): `/scalar` renders, no
  request can be executed from it. Starting Production with the signing key missing exits with a clear
  configuration error.
