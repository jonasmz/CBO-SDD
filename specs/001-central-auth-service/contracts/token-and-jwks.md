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

- Fetch the JWKS at startup and cache it. The cached key set has a **maximum age of 5 minutes**: at 5 minutes
  of age it is no longer fresh and MUST be re-fetched. Consumers SHOULD refresh earlier when useful.
- A token with an unknown `kid` MAY trigger an immediate refresh, limited to at most one forced refresh per
  minute per consumer process (no refresh storms); the token is rejected if the `kid` is still unknown.
- A successful refresh REPLACES the cached key set (never merge, never retain removed keys). After it, a key
  the service no longer publishes MUST NOT be accepted.
- If a refresh attempt fails (endpoint unreachable), the consumer may keep validating with the last
  successfully fetched set for as long as the outage lasts. The 5-minute maximum age does not guarantee that
  a compromised key disappears while the endpoint is unreachable.
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
   `kid` (forced refresh at most once per minute) and, because a cached set is at most 5 minutes old and a
   successful refresh replaces it, stop accepting the old key within 5 minutes after the service stopped
   publishing it, provided their refresh succeeds. This bound holds only while the consumer can retrieve the key set: during an endpoint outage a
   consumer may keep using its last fetched set, including the compromised key. The system makes no claim of
   immediate or bounded global invalidation while consumers cannot obtain the updated set.
3. Consumers with static public keys must be updated manually or by deployment automation; until then they
   may keep accepting tokens signed with the compromised key. Both limitations (static keys, key-endpoint
   outage) MUST be stated in the documentation.
4. Trust in the old key outranks the validity of tokens already issued: once consumers hold the updated
   material, treat every access token signed with the compromised key as untrusted. No revocation service or
   introspection protocol is provided; refresh and session records are not destroyed automatically.

## Refresh token

Opaque string, not a JWT, not to be interpreted by clients. Sent only to `/api/auth/refresh` and
`/api/auth/logout`. Never accepted by consumer APIs.
