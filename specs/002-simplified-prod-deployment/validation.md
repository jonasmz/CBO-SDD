# Final Docker Validation (Phase 8)

Date: 2026-10-02. Feature: `002-simplified-prod-deployment`. No secrets, tokens or key material are recorded here.

## Isolation

- Guard (A0): the host already had the real volumes `authservice-data` and `authservice-dev-data`; they were treated as
  non-disposable and never touched.
- Every production command ran as `docker compose -p simplecheck -f compose.yaml -f validation-override.yaml ...` from an
  isolated copy of the checkout (so the real `.env` was never overwritten). The untracked override only renamed the data
  volume to `simplecheck-data` (and the signing-none volume); `down -v` was used only with the override.
- Spare API port 18090 (only an environment value; the API was reached over the container network).
- Dev flow used the real checkout and its existing `.env`/`COMPOSE_FILE`, with isolated dev data/cache volumes.

## Results

| Task | Check | Status | Evidence (non-secret) |
|---|---|---|---|
| T067 | Simple path, one command: `up -d --build` after copying the production template and filling the 5 required values | PASS | Template keys set: Bootstrap email/password, Jwt issuer/audience, Cors origin, API_PORT, Database__MigrateOnStartup (no PEM/PFX/SIGNING_KEY_DIR/HOST_PROJECT_DIR/LOCAL_*/APP_*) |
| T067 | Only `auth-api` created | PASS | `ps -a --services` = `auth-api`; 0 migrate/dev/dev-api/tools containers |
| T067 | Healthy; auto-migrated; roles; first admin; signing key | PASS | Docker health `healthy`; 2 migrations applied; roles Administrator,Manager,Operator; 1 user; `signing.pem` generated before "Bootstrap administrator created" (log order) |
| T067 | Login / `/api/me` / JWKS | PASS | login HTTP 200; `/api/me` roles `['Administrator']`; JWKS HTTP 200, kid `XGqJcGWjlb1QBT0kw83YcIQDPE6STyBN-5O4iaGlFVk` |
| T068 | Recreate (`up -d --force-recreate auth-api`) on the same volume | PASS | healthy; login 200; refresh with a pre-recreate session 200; 1 user, 1 Administrator membership; Data Protection key files identical (sha256 list); `signing.pem` hash identical; dir `drwx------`, file `-rw-------`, owner 10001; kid identical |
| T069 | Explicit PEM (via `SIGNING_KEY_DIR`) | PASS | healthy; active kid == PEM public-key kid (and != generated kid); `/data/signing` not created; no key text in logs |
| T069 | Explicit PFX (password via environment) | PASS | healthy; active kid == certificate key kid; `/data/signing` not created; no password/key text in logs |
| T070 | Manual mode `Database__MigrateOnStartup=false` | PASS | `up` exits with `Database schema is not up to date; run the migrate service`; `run --rm migrate` rc 0 (roles seeded, 2 migrations); `up -d auth-api` healthy; 0 auto-migration lines in the API log; migrate not left running |
| T071 | `APP_UID=20001 APP_GID=20001`, fresh volume | PASS | `uid=20001 gid=20001`; `/data` writable; `/data/signing` drwx------ and `signing.pem` -rw------- owned 20001:20001; healthy; login 200 |
| T071 | Env independence | PASS | `env -u HOST_PROJECT_DIR -u LOCAL_UID -u LOCAL_GID ... up -d --build` renders, builds, starts, healthy (uid 10001); arbitrary `LOCAL_UID=4242 LOCAL_GID=4243` and a bogus `HOST_PROJECT_DIR` have no effect (uid 10001, healthy) |
| T072 | Dev flow | PASS | `build dev tools` ok; `dotnet restore` rc 0; `dotnet build` 0 errors; tests 4 + 65 passed; `--profile run up -d dev-api`; tools `curl .../health` = `Healthy`; `--profile run down` |
| T073 | Security | PASS | logs: 0 `PRIVATE KEY`, 0 bootstrap-password, 0 key-body matches; image: 0 `*.pem/*.pfx/*.p12/*.key` under /app,/data,/run,/srv,/home,/root,/opt and 0 private-key text under /app,/etc,/home,/root,/srv,/run,/opt; image `/data` empty; runtime uid 10001 (non-root); `git ls-files` shows no tracked `.env`/`secrets/`; `git grep "BEGIN .*PRIVATE KEY"` has 6 hits that are only the header string in docs/spec text and test assertions (0 base64 key-body lines tracked) |
| T074 | Missing key, recovery default | PASS | container exits; message `Persisted signing key is missing but users exist; restore the key from backup or set Jwt__Signing__AllowKeyRegeneration=true to deliberately replace it`; no replacement file created |
| T074 | Corrupt key | PASS | exits with "unreadable, malformed, not an RSA key, or smaller than 2048 bits"; file hash unchanged; also unchanged and still failing with the recovery flag set |
| T074 | Explicit recovery, key absent | PASS | healthy; new kid differs from the previous one; warnings "recovery mode ... Remove it" and "signing key was replaced ... will no longer validate"; key mode -rw-------; after removing the flag: healthy, same kid, 0 recovery warnings (recovery mode not left enabled) |
| T075 | Final automated suite | PASS | 69 tests: 4 architecture + 65 integration, 0 failed, 0 skipped |
| T075 | Cleanup | PASS | `down -v` only with the override; override, temp `.env` copies, signing/PFX material and scratch checkout deleted; 0 leftover `simplecheck`/`p8`/`docsval` volumes, containers, images or networks; `authservice-data` and `authservice-dev-data` still present and untouched; working tree clean |

## Notes

- A failed initialization terminates the process (exit code 139, an unhandled exception) before the server starts, so nothing
  is served; this is intentional fail-fast behavior.
- `validation-override.yaml` and the temporary `.env` existed only in the isolated copy and were never tracked.
- No product defects were found in this phase.
