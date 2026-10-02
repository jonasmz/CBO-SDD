# Feature Specification: Simplified Production Deployment

**Feature Branch**: `002-simplified-prod-deployment`

**Created**: 2026-10-02

**Status**: Draft

**Input**: User description: "Simplify production deployment of the existing Central Auth Service: a default path of copy env template, edit, `docker compose up -d --build`, with automatic first-start initialization (signing key, database schema, bootstrap administrator), while keeping the existing advanced deployment options as opt-in. No change to the domain, API contracts, authentication/authorization models, or architecture."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Deploy a small installation with three steps (Priority: P1)

A small-project operator copies a production environment template, fills in a handful of installation-specific values, and starts the service with Docker Compose. The service initializes itself (signing identity, database schema, fixed roles, first Administrator) and is immediately usable.

**Why this priority**: This is the core value: removing operational friction for the typical installation.

**Independent Test**: On a clean host, run the three-step flow and sign in as the bootstrap Administrator; fetch the public signing keys.

**Acceptance Scenarios**:

1. **Given** a clean host with no prior volume and a template-derived environment file containing only the installation-specific values, **When** the operator runs `docker compose up -d --build`, **Then** the service becomes healthy without any manual signing-material creation, migration command, or identity/permission configuration.
2. **Given** the first startup has completed, **When** the operator signs in with the bootstrap email and password, **Then** authentication succeeds and the account holds the Administrator role.
3. **Given** the first startup has completed, **When** a consumer requests the public key set, **Then** it receives the public key of the automatically provisioned signing identity.
4. **Given** the operator followed the quick deployment, **When** they review required steps, **Then** no development-only settings (repository path, local user/group IDs, dev port) were needed.

---

### User Story 2 - State survives restart and recreation (Priority: P1)

The operator restarts, rebuilds, or recreates the service container and all state remains intact.

**Why this priority**: Without persistence, auto-provisioning would silently rotate keys and lose data.

**Independent Test**: Create a user and a session, note the public key set, recreate the container, and compare.

**Acceptance Scenarios**:

1. **Given** a running installation with users and active refresh/session state, **When** the container is recreated with the persistent volume intact, **Then** users, roles, session state, and protected-data key ring are preserved.
2. **Given** an automatically generated signing identity, **When** the container is restarted or recreated with the volume intact, **Then** the public key set is unchanged and previously issued access tokens remain valid.
3. **Given** the persistent volume is deleted, **When** the service starts again, **Then** it behaves as a fresh installation with a new signing identity (documented as a key replacement event).

---

### User Story 3 - Opt into advanced controls (Priority: P2)

An operator with stricter requirements uses externally managed signing material (RSA PEM or PFX with password), runs migrations explicitly, and/or overrides the runtime user/group identity.

**Why this priority**: Existing installations and advanced operators must not be broken.

**Independent Test**: Configure each advanced option separately and verify its precedence/behavior.

**Acceptance Scenarios**:

1. **Given** an explicit PEM or PFX signing source is configured, **When** the service starts, **Then** that source is used, no automatic key is generated or used, and only one signing source is active.
2. **Given** startup migration is disabled, **When** the operator runs the explicit migration service before starting the API, **Then** the schema is applied and the API starts normally; with it disabled and no migration run, the API does not silently alter the schema.
3. **Given** custom runtime user/group IDs are configured, **When** the service starts, **Then** it runs non-root as those identities with working access to persistent storage.
4. **Given** both an external source and an existing auto-generated key exist, **When** the service starts, **Then** the external source wins and the generated key is left untouched.

---

### User Story 4 - Clear documentation, simple path first (Priority: P2)

An operator reads a single production deployment guide that presents "Quick deployment" first and "Advanced deployment" afterward, including backup/restore and signing-key-loss consequences.

**Why this priority**: The simplification is only realized if the documentation matches it.

**Independent Test**: Review the guide for order, completeness, and absence of mandatory advanced steps in the quick path.

**Acceptance Scenarios**:

1. **Given** the production deployment guide, **When** an operator opens it, **Then** the first section is the quick deployment, containing only essential settings.
2. **Given** the guide, **When** the operator looks for backup scope, **Then** it lists the database, protected-data key ring, and generated signing key (external keys remain under external custody).
3. **Given** the guide, **When** the operator reads about key loss, **Then** it states that tokens signed by the lost key become invalid, consumers must obtain the new public keys, and a new identity is a key replacement event.

---

### Edge Cases

- Bootstrap values are changed after users already exist: no additional user is created.
- Bootstrap password remains in configuration after first start: no impact on behavior; removal is optional hardening.
- Persistent volume contains a database but the generated signing key is missing: a new signing identity is generated and this is treated as a key replacement (documented).
- Signing key file exists but is unreadable or corrupt: startup fails with a clear, secret-free error rather than silently replacing the key.
- Both PEM and PFX configured (conflicting external sources): startup fails clearly (only one active source permitted), consistent with existing behavior.
- Startup migration fails: the service does not begin serving requests and reports a clear error.
- Two instances start against the same volume: unsupported (single-node only); documented.
- Required installation-specific value missing (e.g., issuer, audience, CORS origin, bootstrap credentials on an empty database): startup fails with a clear message rather than using insecure defaults.

## Requirements *(mandatory)*

### Functional Requirements

**Deployment experience**

- **FR-001**: The repository MUST provide a production-specific environment template, separate from the development one, exposing only: API port, bootstrap Administrator email and password, token issuer, token audience, and allowed CORS origins.
- **FR-002**: The production template and quick path MUST NOT require the repository path, local user/group IDs, or the development port setting.
- **FR-003**: A fresh installation MUST be startable with only `docker compose up -d --build` after the template is copied and edited; no other project-specific command may be required.
- **FR-004**: The default production Compose path MUST use the built image and named persistent storage, expose the configured API port, retain health checks, and require no source-checkout bind mounts.

**Runtime identity**

- **FR-005**: The production runtime MUST run non-root. Runtime user/group IDs MUST default to 10001:10001 without operator configuration and remain overridable as an advanced option.

**Signing**

- **FR-006**: When no external signing source is configured, the service MUST generate an RSA signing key of at least 2048 bits using a standard platform cryptographic facility on first startup.
- **FR-007**: The generated key MUST be stored in persistent application storage outside the image, reused on every later start, and stable while that storage remains intact.
- **FR-008**: The generated private key MUST be readable only by the runtime identity, never logged, never exposed via API documentation or any endpoint, and never committed to the repository or baked into the image. Only public material is exposed via the existing public key set.
- **FR-009**: Explicitly configured RSA PEM or PFX+password MUST take precedence over automatic generation; exactly one signing source may be active.

**Database initialization**

- **FR-010**: The service MUST support applying pending schema migrations automatically at startup, enabled by default for the simple deployment path, and MUST be disableable by configuration.
- **FR-011**: When automatic migration is disabled, the existing explicit migration service workflow MUST remain available and working. Migration logic MUST NOT be duplicated between the two modes.
- **FR-012**: Startup order MUST be: migrations (if enabled) → fixed roles ensured → bootstrap Administrator created if required → begin serving requests. A failure at any step MUST prevent serving.

**Bootstrap**

- **FR-013**: The simple path MUST require only the bootstrap email and password. Bootstrap MUST create the first Administrator only when no users exist, and MUST NOT create further users after initialization even if bootstrap values change.
- **FR-014**: Deployment MUST NOT require removing the bootstrap password or recreating the container; documentation MUST recommend removal only as optional hardening.

**Persistence**

- **FR-015**: The simple deployment MUST persist the database, protected-data key ring, and generated signing key across container recreation.

**Security preservation**

- **FR-016**: The feature MUST NOT weaken the password policy, authentication-failure behavior, token validation, authorization, health checks, or CORS restrictions, and MUST NOT expose development tooling by default. Wildcard/unrestricted CORS MUST NOT be the default.
- **FR-017**: No secret (private key, bootstrap password) may be added to the repository or the image.

**Documentation**

- **FR-018**: The production deployment guide MUST be restructured with "Quick deployment" first (essential settings only), followed by "Advanced deployment" covering external PEM/PFX signing, explicit migration service, custom runtime IDs, secret mounts, controlled key rotation, reverse proxy configuration, and other operational controls.
- **FR-019**: Documentation MUST state the consequences of losing the generated signing key: previously issued access tokens can no longer be validated, consumers must obtain the replacement public key set, and a new signing identity is a key replacement event.
- **FR-020**: Backup/restore documentation MUST be updated so that, with automatic signing, backup scope includes the database, protected-data key ring, and generated signing key; externally managed keys remain under external custody. Documentation MUST clearly warn that deleting the persistent volume destroys this state.
- **FR-021**: Documentation MUST state that TLS is terminated at a reverse proxy for Internet-facing use, and that direct local/LAN deployment is possible; no reverse proxy is provided by this feature.

**Compatibility**

- **FR-022**: Existing installations using explicit PEM, explicit PFX, the manual migration service, or custom runtime IDs MUST continue to work without mandatory changes.

**Testing**

- **FR-023**: Automated tests MUST cover, risk-based and without a coverage target: automatic key creation, reuse of the generated key, precedence of external signing material, automatic migration, bootstrap behavior after automatic migration, and configuration defaults.

### Key Entities

- **Production environment template**: Operator-facing file listing only installation-specific values.
- **Persistent application volume**: Holds the database, protected-data key ring, and generated signing key.
- **Generated signing identity**: Auto-provisioned RSA key pair; private part persisted privately, public part published via the existing public key set.
- **Startup mode settings**: Signing source selection (auto vs external) and migration mode (automatic vs explicit).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fresh installation is configured and started with copy-template, edit, and `docker compose up -d --build` without manually creating signing material.
- **SC-002**: On a fresh installation in simple mode, the current schema is applied automatically with no extra command.
- **SC-003**: The first Administrator can authenticate immediately after the first successful startup.
- **SC-004**: Restarting or recreating the container preserves users, roles, refresh/session state, protected-data keys, and the generated signing identity.
- **SC-005**: The public key set is identical before and after container recreation while the volume is intact.
- **SC-006**: The production service runs non-root without APP_UID/APP_GID being set.
- **SC-007**: The repository path setting and local user/group ID settings are not required for the normal production runtime.
- **SC-008**: An operator can still opt into externally managed PEM/PFX signing, which takes precedence over generation.
- **SC-009**: An operator can still opt into explicit migration execution instead of automatic migration.
- **SC-010**: No private signing key, bootstrap password, or other secret exists in the repository or image.
- **SC-011**: The production guide presents the quick path before the advanced path.
- **SC-012**: The complete simple first install requires no project-specific command other than Docker Compose after editing the environment configuration.

## Assumptions

- Out of scope and unchanged: identity, email/password login, token format and validation, public key set, roles, refresh tokens, password reset, authorization policies, API documentation behavior, security-event logging, SQLite as the default store, and the layered/vertical-slice architecture.
- Out of scope: OAuth/OIDC, external secret managers, Kubernetes, Swarm, Vault, PostgreSQL, distributed locking, multi-node support, new business functionality, reverse proxy implementation, automatic certificate management.
- Deployments are single-node, single-instance per persistent volume.
- Deployment/signing/persistence mechanics live in Infrastructure/API layers; Domain and Application remain unaffected, with no abstractions added for hypothetical deployment systems.
- Existing explicit migration service, PEM/PFX configuration, and bootstrap mechanism are reused rather than reimplemented.
- Issuer, audience, CORS origin, and (for empty databases) bootstrap credentials are inherently installation-specific and have no insecure defaults.
- The exact format and path of the generated key within the persistent volume (e.g., under a signing subfolder) is an implementation decision.
- The development environment template and development tooling workflows keep their current behavior.
