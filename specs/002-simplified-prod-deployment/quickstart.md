# Quickstart: End-to-End Validation

All steps run from a clean checkout on a host with Docker only. Use a throwaway Compose project (`-p simplecheck`) so the real volume is untouched.

## A. Simple path (SC-001…SC-007, SC-012)
1. `docker compose -p simplecheck down -v` (clean volume).
2. `cp .env.production.example .env`; set the five required values.
3. `docker compose -p simplecheck up -d --build` — no other command.
4. Wait for `healthy`.
5. `POST /api/auth/login` as the bootstrap Administrator → 200.
6. `GET /api/me` with the access token → Administrator role.
7. `GET /.well-known/jwks.json` (use the repo's actual JWKS route) → record `kid`.
8. `docker compose -p simplecheck up -d --force-recreate auth-api`.
9. Log in again → 200.
10. User and refresh-session state still present.
11. JWKS `kid` identical; a pre-recreate access token still validates.
12. `docker run --rm -v simplecheck_authservice-data:/data alpine ls -l /data/signing` → `signing.pem` present, mode `-rw-------`.

## B. Compatibility (SC-008, SC-009)
13. Explicit PEM (`SIGNING_KEY_DIR` + `Jwt__Signing__PrivateKeyPemPath`): starts, `kid` ≠ generated, no new `/data/signing` file.
14. Explicit PFX with password: starts.
15. `Database__MigrateOnStartup=false`: `up` fails with "run the migrate service"; `docker compose run --rm migrate` then `up` succeeds.
16. `APP_UID=20001 APP_GID=20001` rebuild with a fresh volume: starts, process uid is 20001.
17. `HOST_PROJECT_DIR` unset: `docker compose config` and `up` succeed.
18. `LOCAL_UID/LOCAL_GID` set to arbitrary values: no effect on `auth-api`.

## C. Security (SC-006, SC-010)
19. `docker compose logs auth-api` contains no PEM markers/password.
20. `docker run --rm --entrypoint sh <image> -c 'find / -name "*.pem"'` finds no signing key in the image.
21. `git ls-files` / `git grep -n "BEGIN .*PRIVATE KEY"` finds nothing; `.env` is ignored.
22. `docker compose exec auth-api id -u` ≠ 0.

## D. Fail-safe
Delete `/data/signing/signing.pem` with users present → container exits with the missing-key error; set `Jwt__Signing__AllowKeyRegeneration=true` → starts with a new `kid` and a warning.
