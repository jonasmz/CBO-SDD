# Token and Key Distribution Contract

## Access token (JWT, RS256)

Header: `{ "alg": "RS256", "typ": "JWT", "kid": "<key id>" }`

| Claim | Meaning |
|---|---|
| `sub` | user id (GUID) |
| `role` | one claim per role: `Administrator`, `Manager`, `Operator` (JSON array when several) |
| `iss` | configured issuer string |
| `aud` | configured audience string shared by consumer APIs |
| `iat`, `nbf`, `exp` | issue, not-before, expiry (default lifetime 15 minutes) |
| `jti` | unique token id |

No email, no profile data, no password-related data. Consumer validation MUST check signature, `iss`,
`aud`, and lifetime (clock skew at most 1 minute), with `NameClaimType = "sub"`,
`RoleClaimType = "role"`, and inbound claim mapping disabled.

## JWKS: `GET /.well-known/jwks.json`

```json
{ "keys": [ { "kty": "RSA", "use": "sig", "alg": "RS256", "kid": "...", "n": "...", "e": "AQAB" } ] }
```

- Contains the current signing key's public part and every configured previous public key.
- Never contains private parameters (`d`, `p`, `q`, `dp`, `dq`, `qi`).
- `Cache-Control: public, max-age=300`.

## Alternative distribution

The same public key may be given to consumers as a PEM or certificate (public part) file. Consumers
using a static key do not need network access to the auth service, but must update the file on rotation.

## Rotation contract

1. Operator provisions the new private key and sets it as `Current`; the old public key moves to
   `PreviousPublicKeys`. Restart the service.
2. New tokens carry the new `kid`; consumers resolve keys by `kid` (JWKS) or have both keys configured.
3. After at least one access-token lifetime plus skew, remove the old public key.

## Refresh token

Opaque string, not a JWT, not to be interpreted by clients. Sent only to `/api/auth/refresh` and
`/api/auth/logout`. Never accepted by consumer APIs.
