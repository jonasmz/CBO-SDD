# Production deployment

The production `Dockerfile` publishes in an SDK stage and runs on the ASP.NET runtime image. It contains no
.NET SDK, EF CLI, SQLite CLI, curl, package manager, or repository tools image. The upstream ASP.NET runtime
currently includes an OpenSSL executable as a runtime dependency; it is not used as the project tooling
surface. The API and migration mode run as numeric, non-root
`APP_UID:APP_GID`; Compose takes these values from authoritative `.env`/deployment configuration (documented
defaults 10001:10001). Dockerfile ARG defaults are only direct-build fallbacks. These values are unrelated
to development-only `LOCAL_UID:LOCAL_GID`.

## Configuration and secrets

Keep `.env` out of version control and mode 0600 where supported. Environment variables may be visible via
container inspection. Prefer mounted secret files under `/run/secrets/config` (ASP.NET key-per-file names
such as `Jwt__Signing__CertificatePassword`) or the deployment platform's existing secret mechanism for PFX
and bootstrap passwords. The migration service needs neither signing material nor the PFX password.

Private signing material must remain outside the repository and image. For Compose, use a production path
outside the checkout, set `SIGNING_KEY_DIR`, and mount it read-only. Provision as an operator:

```bash
install -d -m 0750 -o <operator-user> -g "${APP_GID}" /srv/auth-service/signing
install -m 0640 -o <operator-user> -g "${APP_GID}" current.pem /srv/auth-service/signing/current.pem
```

Setting the group may require operator privileges. Only the provisioning identity and service process should
read it; the container never needs root. With rootless Docker/userns-remap, use a deployment-provided
runtime-readable secret, or copy an operator-owned 0600 secret into a container-visible location owned by
the runtime user before startup.

Use exactly one current signing source: an RSA private PEM (>=2048 bits), or a PFX plus externally supplied
password. Certificate expiry is logged but not enforced; monitor and rotate before expiry. Distribute only
JWKS/public PEM/certificate material to consumers.

For local validation only—not production provisioning—generate a throwaway key in the ignored `secrets/`
directory with the repository tools service:

```bash
mkdir -p secrets
docker compose run --rm -T --user "${LOCAL_UID:-1000}:${LOCAL_GID:-1000}" \
  -v "${SIGNING_KEY_DIR:-${HOST_PROJECT_DIR}/secrets}:/out" tools \
  sh -c 'openssl genrsa -out /out/signing.pem 2048 && chmod 0644 /out/signing.pem'
```

## Deployment sequence

1. Configure issuer, audience, bootstrap pair if the database is empty, CORS, runtime IDs, database volume,
   and signing mount.
2. Run `docker compose build auth-api migrate`.
3. Run `docker compose run --rm migrate`.
4. Start with `docker compose up -d auth-api` and verify `/health` through the Compose network.
5. Remove bootstrap password configuration after the first Administrator exists and recreate the container.

The SQLite and Data Protection paths persist on `authservice-data`. Put the service behind a
TLS-terminating reverse proxy; enable `ForwardedHeaders__Enabled=true` only with a trusted proxy topology.
Configure explicit CORS origins.

Production `/openapi/v1.json` and `/scalar` remain anonymously readable by application design. Scalar hides
request execution and client controls; the document has no servers or security schemes. Restrict anonymous
documentation at the reverse proxy if organizational policy requires it. `/health` is anonymous and returns
only `Healthy`/`Unhealthy`.

Planned rotation publishes the old public key in `PreviousPublicKeyPaths` during overlap. Emergency
replacement removes the compromised key immediately; see operations and consumer integration guides.
