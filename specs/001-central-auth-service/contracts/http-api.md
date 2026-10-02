# HTTP API Contract

Base path as listed. JSON bodies, `application/json`. Errors use RFC 9457 Problem Details
(`application/problem+json`). Authenticated routes need `Authorization: Bearer <access token>`.
Role route values are validated against `Administrator | Manager | Operator`.

## Authentication (public unless stated)

| Method | Path | Auth | Rate limited |
|---|---|---|---|
| POST | `/api/auth/login` | none | yes |
| POST | `/api/auth/refresh` | none (refresh token in body) | yes |
| POST | `/api/auth/logout` | none (refresh token in body) | no |
| POST | `/api/auth/reset-password` | none (reset code in body) | yes |

### POST /api/auth/login
Request: `{ "email": string, "password": string }`
- 200: `{ "accessToken": string, "expiresInSeconds": number, "refreshToken": string, "refreshTokenExpiresAt": ISO-8601 }`
- 401: generic Problem Details (`title: "Invalid credentials"`) for unknown email, wrong password,
  disabled account, or lockout. Bodies and status identical.
- 400: validation problem (missing or malformed fields; never mentions account existence)
- 429: rate limit exceeded

### POST /api/auth/refresh
Request: `{ "refreshToken": string }`
- 200: same shape as login (new access token and new refresh token; the presented token is now invalid)
- 401: unknown, expired (individual lifetime or session absolute lifetime), rotated, revoked token or disabled
  user. Presenting a rotated or revoked token additionally revokes the whole session. A new refresh token
  never expires later than its session's absolute expiration.

### POST /api/auth/logout
Request: `{ "refreshToken": string }`
- 204: the session of that token is revoked (idempotent; unknown tokens also return 204)

### POST /api/auth/reset-password
Request: `{ "email": string, "resetCode": string, "newPassword": string }`
- 204: password changed, all sessions of the user revoked
- 400: password does not meet the rule (checked first, independent of code or account state)
- 401: invalid, expired or already-used code, unknown email, or a disabled account (same response); a failed
  attempt does not consume the code

## Current identity

### GET /api/me (any authenticated user)
- 200: `{ "id": guid, "email": string, "roles": string[] }`
- 401: missing or invalid token

## User administration (policy `RequireAdministrator`; 401 unauthenticated, 403 Manager/Operator)

| Method | Path | Success |
|---|---|---|
| GET | `/api/users` | 200 `{ items: UserDto[], total, skip, take }`; query `skip` (default 0), `take` (default 50, max 200) |
| GET | `/api/users/{id}` | 200 `UserDto`; 404 unknown |
| POST | `/api/users` | 201 `UserDto` + `Location`; 400 invalid email/password/roles (at least one supported role required); 409 email exists |
| POST | `/api/users/{id}/enable` | 204 |
| POST | `/api/users/{id}/disable` | 204; revokes all sessions; 409 if last enabled Administrator |
| POST | `/api/users/{id}/password-reset` | 200 `{ "resetCode": string, "expiresAt": ISO-8601 }` shown once; allowed for a disabled account (the code cannot be redeemed while it stays disabled) |
| PUT | `/api/users/{id}/roles/{role}` | 204 (idempotent); 400 unsupported role; 404 unknown user |
| DELETE | `/api/users/{id}/roles/{role}` | 204; 400 unsupported role; 409 last role or last enabled Administrator |

`UserDto`: `{ "id": guid, "email": string, "isEnabled": bool, "roles": string[], "createdAt": ISO-8601 }`
`POST /api/users` body: `{ "email": string, "password": string, "roles": string[] }`

## Public metadata and operations

| Method | Path | Notes |
|---|---|---|
| GET | `/.well-known/jwks.json` | public verification keys (see token-and-jwks.md); cacheable |
| GET | `/health` | anonymous; 200 `Healthy` / 503 `Unhealthy`; no internal detail |
| GET | `/openapi/v1.json` | OpenAPI document |
| GET | `/scalar` | API reference UI (interactive only in Development) |

## Error conventions

- 400 validation (`errors` map), 401 unauthenticated, 403 forbidden, 404 missing, 409 state conflict,
  429 throttled, 500 unhandled (generic Problem Details; no stack traces or internals in Production).
- Unhandled exceptions are logged without request bodies.
