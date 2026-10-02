# Token and Key Distribution Contract

## Access token (JWT, RS256)

Header: `{ "alg": "RS256", "typ": "JWT", "kid": "<key id>" }`

| Claim | Meaning |
|---|---|
| `sub` | user id (GUID) |
| `role` | one claim per role: `Administrator`, `Manager`, `Operator` (JSON array when several) |
| `iss` | configured issuer string |
| `aud` | configured audience string shared by consumer APIs |
| `iat`, `exp` | issue time and expiry (default lifetime 15 minutes) |

No other claims (no `jti`, `nbf`, email, profile or password-related data). Consumer validation MUST check signature, `iss`,
`aud`, and lifetime (clock skew at most 1 minute), with `NameClaimType = "sub"`,
`RoleClaimType = "role"`, and inbound claim mapping disabled.

## JWKS: `GET /.well-known/jwks.json`

```json
{ "keys": [ { "kty": "RSA", "use": "sig", "alg": "RS256", "kid": "...", "n": "...", "e": "AQAB" } ] }
```

- Contains the current signing key's public part and every configured previous public key.
- Never contains private parameters (`d`, `p`, `q`, `dp`, `dq`, `qi`).
- `Cache-Control: public, max-age=300`.

## Consumer key-retrieval behavior

- Fetch the JWKS at startup and refresh at most every 5 minutes, or immediately when a token carries an
  unknown `kid` (at most once per minute to avoid amplification).
- On every refresh replace the cached key set with the published one (never merge), so a key removed by the service is dropped at the next refresh.
- If the endpoint is unreachable, keep validating with the last fetched keys.
- If no keys have ever been fetched, reject every token (fail closed).
- A consumer using a static public key file needs no network access but must update it on rotation.

## Alternative distribution

The same public key may be given to consumers as a PEM or certificate (public part) file. Consumers
using a static key do not need network access to the auth service, but must update the file on rotation.

## Rotation contract

1. Operator provisions the new private key and sets it as `Current`; the old public key moves to
   `PreviousPublicKeys`. Restart the service.
2. New tokens carry the new `kid`; consumers resolve keys by `kid` (JWKS) or have both keys configured.
3. After at least one access-token lifetime plus skew, remove the old public key.

## Emergency key replacement (suspected or confirmed compromise)

Different from planned rotation: no overlap period.

1. The compromised key stops signing: provision a new private key as `Current`, remove the compromised
   public key from `PreviousPublicKeys` (do not keep it), and restart the service. New tokens carry the new
   `kid`; existing refresh tokens and sessions keep working and receive access tokens signed by the new key.
2. The key endpoint no longer lists the compromised key. Consumers using it adopt the new key on an unknown
   `kid` and drop the old one at their next refresh (at most 5 minutes later).
3. Consumers with static public keys must be updated manually or by deployment automation; until then they
   may keep accepting tokens signed with the compromised key. This limitation MUST be stated in the
   documentation.
4. Trust in the old key outranks the validity of tokens already issued: once consumers hold the updated
   material, treat every access token signed with the compromised key as untrusted. No revocation service or
   introspection protocol is provided; refresh and session records are not destroyed automatically.

## Refresh token

Opaque string, not a JWT, not to be interpreted by clients. Sent only to `/api/auth/refresh` and
`/api/auth/logout`. Never accepted by consumer APIs.
