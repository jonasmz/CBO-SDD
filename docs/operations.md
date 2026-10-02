# Operations

## Health and logs

```bash
docker compose run --rm -T tools curl -fsS http://auth-api:8080/health
docker compose logs auth-api
```

Health returns only `Healthy` or `Unhealthy`. Logs include failed sign-in, refresh-token reuse/session
expiry, bootstrap outcome, signing `kid` and certificate expiry, enabled-state and role changes, and reset
issuance/redemption. They identify users/families where needed but must never contain passwords, access or
refresh tokens, reset codes, private keys, or PFX passwords. Do not enable request-body logging.

## First Administrator and migrations

On an empty migrated store, configure `Bootstrap__AdminEmail` and a policy-compliant
`Bootstrap__AdminPassword`, then start the API. Confirm `Bootstrap administrator created`. Restart and
confirm `Bootstrap skipped: users already exist`; this proves initialization is closed. Remove the bootstrap
password from external configuration and recreate the container.

Apply migrations before normal startup with `docker compose run --rm migrate`. It seeds missing fixed roles
and is idempotent. Normal Production startup with missing/pending schema exits with
`Database schema is not up to date; run the migrate service`; it never migrates silently.

## Backup and recovery

Follow [database.md](database.md): stop the API, copy `/data/auth.db` and `/data/keys` with the repository
tools image, protect/checksum the backup, and perform a restore drill that verifies users, roles, enabled
states, health, and login. Signing-key custody is separate from the database backup.

## Planned signing-key rotation

1. Provision a new private key as current outside the checkout/image.
2. Export the old public part and configure it in `Jwt__Signing__PreviousPublicKeyPaths__0`.
3. Restart; verify JWKS publishes both keys and new tokens carry the new `kid`.
4. Wait at least one access-token lifetime plus one-minute skew.
5. Remove the old public key, restart, and confirm JWKS no longer publishes it.

## Emergency signing-key replacement

1. Provision replacement key B as current immediately.
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
- Startup schema error: run the migration service; never use `EnsureCreated`.
- SQLite locked/write contention: ensure only one API instance writes the volume.
