# Operations

## Health and logs

```bash
docker compose ps                                              # STATUS shows (healthy)
docker compose exec -T auth-api dotnet AuthService.Api.dll --healthcheck && echo healthy
docker compose logs auth-api
```

Health returns only `Healthy` or `Unhealthy`. Logs include failed sign-in, refresh-token reuse/session
expiry, bootstrap outcome, signing `kid` and certificate expiry, enabled-state and role changes, and reset
issuance/redemption. They identify users/families where needed but must never contain passwords, access or
refresh tokens, reset codes, private keys, or PFX passwords. Do not enable request-body logging.

## First Administrator and migrations

On an empty database the API creates the first Administrator from `Bootstrap__AdminEmail` and a
policy-compliant `Bootstrap__AdminPassword`; confirm `Bootstrap administrator created` in the logs. On later
starts you will see `Bootstrap skipped: users already exist`: from then on the bootstrap values are ignored
completely (not even validated), and changing them never creates another account. Removing
`Bootstrap__AdminPassword` from `.env` and recreating the container (`docker compose up -d --force-recreate
auth-api`) afterwards is **optional hardening** to keep fewer secrets on disk; it is not required for the
installation to be usable. (If the database were ever reset to empty, startup would then fail clearly until the
pair is configured again.)

With the production template (`Database__MigrateOnStartup=true`) migrations are applied automatically at start.
With manual migrations (`false` or absent) run `docker compose run --rm migrate` first; otherwise startup exits
with `Database schema is not up to date; run the migrate service` and never migrates silently.

## Backup, restore and partial restores

Follow [database.md](database.md). With the automatically generated signing key, a complete backup is the
SQLite database (`/data/auth.db`), the Data Protection key ring (`/data/keys`) **and** the signing key
(`/data/signing/signing.pem`), all from the same operational snapshot (stop the API, copy, start). Externally
managed PEM/PFX keys remain under their own custody and are not in that backup. Perform a restore drill that
verifies users, roles, enabled states, health, login and an unchanged JWKS `kid`.

Restoring only part of the state is unsupported. What happens:

| Restored | Missing | Result |
|---|---|---|
| Database (users exist) | Signing key | Startup fails: `Persisted signing key is missing but users exist ...` (see recovery below) |
| Database + key | Data Protection keys | Starts, but protected data (such as outstanding password-reset codes) may stop validating |
| Database and key from different snapshots | - | Starts: there is no startup consistency check. Tokens and users may not correspond; treat it as an unsupported, incoherent restore |

## Losing the generated signing key

If `/data/signing/signing.pem` is lost, access tokens signed with it can no longer be validated, a new key
produces a new `kid`, and consumers must refresh their JWKS: **this is a key-replacement event**.

Preferred fix: restore the file from backup (same snapshot as the database). If it cannot be restored, recover
deliberately:

1. Set `Jwt__Signing__AllowKeyRegeneration=true` in `.env` and recreate the container.
2. Startup generates a replacement key only because the file is absent, and logs a warning that the signing key
   was replaced and old tokens will no longer validate.
3. Verify the new `kid` in `/.well-known/jwks.json` and that consumers pick it up.
4. Remove the flag and recreate the container. While it is set a warning is logged on every start.

The flag never overwrites an existing key and never repairs a corrupt or unreadable one: that still fails
startup, and the file must be restored or fixed by an operator.

## Planned signing-key rotation

1. Provision a new private key as the current external PEM/PFX (outside the checkout/image). To leave the generated key, extract its public part first: `openssl pkey -in signing.pem -pubout -out old-public.pem`.
2. Export the old public part and configure it in `Jwt__Signing__PreviousPublicKeyPaths__0`.
3. Restart; verify JWKS publishes both keys and new tokens carry the new `kid`.
4. Wait at least one access-token lifetime plus one-minute skew.
5. Remove the old public key, restart, and confirm JWKS no longer publishes it.

## Emergency signing-key replacement

1. Provision replacement key B as current immediately (an external PEM/PFX; for the generated key, remove the file and use the recovery procedure above).
2. Remove compromised key A from all previous-key configuration—there is no overlap.
3. Restart, confirm JWKS omits A and includes B, and confirm fresh tokens carry B's `kid`.
4. Consumers successfully refresh and replace (never merge) their complete cached key set. Once updated,
   treat every token signed by A as untrusted. Refresh sessions are not destroyed automatically.

JWKS consumers should drop A within five minutes after the service stops publishing it only if retrieval
succeeds. If consumer APIs cannot retrieve the updated authoritative public key set, there is no bounded
guarantee that a compromised cached key disappears within five minutes. Static-key consumers continue to
accept A until an operator or deployment automation updates them.

Monitor certificate/key expiration externally and rotate before expiry; startup logs certificate expiry but
does not reject expired/not-yet-valid certificates.

## Troubleshooting

- 401 login: wrong/unknown credential, disabled user, or lockout; response is intentionally generic.
- 401 bearer request: expired/tampered token, wrong issuer/audience, unknown `kid`, or excessive clock skew.
- 401 refresh: unknown, expired, rotated/revoked token, expired absolute session, or disabled user. Sign in
  again after the absolute cap. Reuse during a live session revokes the family.
- 403: token is valid but lacks the required role; `/api/users` requires Administrator.
- 429: per-source fixed-window limit reached (default 10 requests/minute); wait for the window and examine
  client/proxy address handling. Identity lockout separately defaults to five failed attempts/15 minutes.
- Unknown `kid`: refresh JWKS (forced at most once/minute/process), replace the cache, and reject if absent.
- Key endpoint outage: with no prior keys fail closed; with a cached set, availability may continue but
  compromised-key removal has no bounded guarantee.
- Startup schema error (manual migration mode): run the migration service; never use `EnsureCreated`.
- Startup says the persisted signing key is missing: restore it from backup, or see "Losing the generated signing key".
- Startup says the persisted signing key is unreadable/malformed: it is intentionally never replaced; restore it from backup or fix its permissions.
- SQLite locked/write contention: ensure only one API instance writes the volume.
