# Authentication and sessions

Terminate TLS in a reverse proxy and never expose the service directly to untrusted networks.

## Login

```http
POST /api/auth/login
Content-Type: application/json

{"email":"admin@example.test","password":"<secret>"}
```

A successful response contains `accessToken`, `expiresInSeconds`, `refreshToken`, and
`refreshTokenExpiresAt`. The access-token default is 15 minutes (configurable 1–60). Unknown email, wrong
password, disabled account, and lockout all return the same generic 401 response; the unknown-email path
still performs dummy password verification. Five failed attempts lock an account for 15 minutes by default.
Login, refresh, and password-reset endpoints are also limited by source address to 10 requests per minute
by default.

Passwords require at least eight characters, an uppercase letter, a lowercase letter, and a digit. They
are handled by ASP.NET Core Identity and must never be logged or returned.

## Refresh and rotation

```http
POST /api/auth/refresh
Content-Type: application/json

{"refreshToken":"<opaque token>"}
```

Refresh tokens are opaque and stored only as hashes. A valid refresh returns a new access token and a new
refresh token; the submitted token is immediately unusable. Reuse of a rotated/revoked token while the
session is valid returns 401 and revokes the entire token family. A disabled user cannot refresh.

The individual refresh lifetime defaults to 7 days (1–90). Rotation is sliding, but the absolute session
lifetime defaults to 30 days (1–365 and never shorter than the individual lifetime). Rotation never moves
the absolute deadline. At the cap, sign in again. Absolute expiration is checked before reuse state, so an
old rotated token submitted after the cap is treated as an expired session without additional reuse action.

## Logout

`POST /api/auth/logout` with `{"refreshToken":"<opaque token>"}` revokes that renewable session and returns
204. It is idempotent; an unknown or already-revoked token also returns 204.

## Password reset

An Administrator issues a one-time code with `POST /api/users/{id}/password-reset`; the code is displayed
once and expires after 24 hours by default (1–72). Issuance is allowed for disabled accounts. Redemption:

```http
POST /api/auth/reset-password
Content-Type: application/json

{"email":"user@example.test","resetCode":"<one-time code>","newPassword":"<new secret>"}
```

Password rules are checked first. A successful reset returns 204 and revokes all sessions. Unknown email,
invalid/expired/used code, and disabled account return the same 401. A failed redemption—including while
disabled—does not consume the code.
