# Quickstart: End-to-End Validation

All steps run from a clean checkout on a host with Docker only.

**Isolation (A0)**: `compose.yaml` pins the data volume with `name: authservice-data`, so `-p` alone does NOT isolate state and `down -v` would delete a real installation's data. Use a disposable validation host, or abort if `docker volume inspect authservice-data` succeeds. Create an untracked `validation-override.yaml` containing `volumes: { authservice-data: { name: simplecheck-data } }` and use `docker compose -p simplecheck -f compose.yaml -f validation-override.yaml ...` for every command below (abbreviated `dc`). Delete the override at the end.

## A. Simple path (SC-001…SC-007, SC-012)
1. `dc down -v` (clean `simplecheck-data` volume).
2. `cp .env.production.example .env`; set the five required values.
3. `dc up -d --build` — no other command; `dc ps --services` lists only `auth-api`.
4. Wait for `healthy`.
5. `POST /api/auth/login` as the bootstrap Administrator → 200.
6. `GET /api/me` with the access token → Administrator role.
7. `GET /.well-known/jwks.json` → record `kid`.
8. `dc up -d --force-recreate auth-api`.
9. Log in again → 200.
10. User and refresh-session state still present.
11. JWKS `kid` identical; a pre-recreate access token still validates.
12. (plain `docker`, not `dc`) `docker run --rm -v simplecheck-data:/data alpine ls -l /data/signing` → `signing.pem` present, mode `-rw-------`.

## B. Compatibility (SC-008, SC-009)
13. Explicit PEM (`SIGNING_KEY_DIR` + `Jwt__Signing__PrivateKeyPemPath`), `dc up -d`: starts, `kid` ≠ generated, no new `/data/signing` file.
14. Explicit PFX with password, `dc up -d`: starts.
15. `Database__MigrateOnStartup=false`: `dc up -d` fails with "run the migrate service"; `dc run --rm migrate` then `dc up -d` succeeds.
16. `APP_UID=20001 APP_GID=20001` `dc up -d --build` with a fresh volume: starts, process uid is 20001.
17. `HOST_PROJECT_DIR` unset: `docker compose config` and `up` succeed.
18. `LOCAL_UID/LOCAL_GID` set to arbitrary values: no effect on `auth-api`.

## C. Security (SC-006, SC-010)
19. `docker compose logs auth-api` contains no PEM markers/password.
20. `docker run --rm --entrypoint sh <image> -c 'find / -name "*.pem"'` finds no signing key in the image.
21. `git ls-files` / `git grep -n "BEGIN .*PRIVATE KEY"` finds nothing; `.env` is ignored.
22. `docker compose exec auth-api id -u` ≠ 0.

## D. Fail-safe
Delete `/data/signing/signing.pem` with users present → container exits with the missing-key error; set `Jwt__Signing__AllowKeyRegeneration=true` → starts with a new `kid` and a warning.
