# Feature Specification: Central Authentication and Authorization Service

**Feature Branch**: `001-central-auth-service`

**Created**: 2026-10-02

**Status**: Draft

**Input**: User description: "Build a standalone authentication and authorization service that acts as the central identity authority for a shared frontend and multiple independent REST business APIs."

## Clarifications

### Session 2026-10-02

- Q: What is the access-token lifetime? → A: 15 minutes, configurable.
- Q: Are refresh tokens the renewal mechanism, how long do they last, and is rotation required? → A: Yes, opaque refresh tokens; 7-day lifetime, configurable; single-use with mandatory rotation on every renewal.
- Q: What happens when an already-rotated or revoked refresh token is presented? → A: Request rejected and the entire session (token family) it belongs to is revoked.
- Q: Must JWT signing use an X.509 certificate? → A: No. Asymmetric signing with an externally supplied key pair (certificate-based or raw key) is acceptable; the key mechanism is configuration, not code. Private key stays in the service; consumers get public verification material only.
- Q: How is the first Administrator bootstrapped, and when is it allowed? → A: Via externally supplied configuration (initial email and password) applied at startup; allowed only when the store contains zero users; unavailable permanently once any user exists, and configuration is then ignored.
- Q: Must every user have at least one role, and can a user hold several? → A: Every user MUST have at least one role at all times; users MAY hold multiple roles simultaneously.
- Q: What is the minimum administrative password reset workflow? → A: Administrator generates a single-use, time-limited reset code shown once to the Administrator and delivered to the user out of band; the user redeems it to set a new password. No email infrastructure.
- Q: Does logout revoke only the current session or all sessions? → A: Only the current session. Disabling a user or completing a password reset revokes all of that user's sessions.
- Q: What minimum rule must a new password meet? → A: At least 8 characters with at least one upper-case letter, one lower-case letter and one digit.

### Session 2026-10-02 (analysis remediation)

- Q: Which sign-in throttle limits apply? → A: Per account, 5 consecutive failures refuse further attempts on that account for 15 minutes; per source address, at most 10 requests per minute to sign-in, renewal and reset-redemption endpoints (excess gets 429). All values configurable.
- Q: Which claims does an access token carry? → A: `sub`, `role`, `iss`, `aud`, `iat`, `exp` only.
- Q: What must the service log about security events? → A: Failed sign-ins (without the email or password), refresh-token reuse detection, user enable/disable, role assignment/removal, password-reset issuance and redemption, bootstrap outcome, and at startup the signing key identifier and, for certificates, the expiry date. Never secrets.
- Q: What happens when the signing certificate expires? → A: The service does not enforce certificate validity dates (it uses the key); the expiry date is logged at startup and operators must rotate before expiry as documented.
- Q: Who provides transport security, and how far is an Administrator trusted in a password reset? → A: TLS is terminated by the deployment (a reverse proxy); the service listens on plain HTTP behind it and the documentation forbids exposing it directly to untrusted networks. An Administrator who issues a reset code can set that user's password, so Administrators are trusted accounts.
- Q: What must a consumer API do when it cannot reach the key endpoint? → A: Keep validating with the last keys it fetched; with no keys ever fetched, reject all tokens. Consumers refresh keys at most every 5 minutes, or on an unknown key identifier (at most once per minute).

### Session 2026-10-02 (checklist resolution)

- Q: Can a disabled user redeem a password-reset code? → A: No. An Administrator may still issue a code for a disabled account, but redemption fails with the same generic failure as an invalid code while the account is disabled, revealing no account state; a failed redemption does not consume the code; re-enabling does not sign the user in.
- Q: How must private signing material be protected in production? → A: Readable only by the service process and the provisioning operator identity, mounted read-only where practical, ownership and permissions that block unrelated host users and processes, never in an image or the repository; a PFX password comes only from external secret configuration and a missing or invalid PFX credential fails production startup clearly; PEM keys follow equivalent rules.
- Q: What happens on suspected or confirmed signing-key compromise? → A: Emergency replacement, distinct from planned rotation: the compromised key stops signing immediately, its public key is removed from publication as soon as operationally possible (no overlap), consumers using the key endpoint drop it at their next refresh, consumers with static keys keep accepting it until updated (documented limitation), and invalidating trust in the old key takes priority over already-issued access tokens. No revocation service or token introspection is introduced.
- Q: What are the refresh-token and session lifetime semantics? → A: Each refresh token lives 7 days by default; renewal issues a new token with a fresh 7 days (sliding), but the session also has an absolute maximum of 30 days by default from the original sign-in that renewal cannot extend. Both are configurable, validated at startup, and the absolute lifetime must be at least the individual lifetime.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sign in and call a consumer API (Priority: P1)

A registered, enabled user signs in with their email and password and receives an access token. The shared frontend presents that token to any independent consumer API, which accepts or rejects it on its own, without contacting the authentication service or its data store.

**Why this priority**: This is the core value of the service: one identity authority trusted by many independent APIs. Nothing else is useful without it.

**Independent Test**: Sign in with a valid account, then validate the issued token in a separate consumer API configured only with public verification information. Repeat with a wrong password, an unknown email and a disabled account.

**Acceptance Scenarios**:

1. **Given** an enabled user with known credentials, **When** they sign in with email and password, **Then** they receive a signed, short-lived access token.
2. **Given** a token issued by the service, **When** an independent consumer API validates it using only public verification information, **Then** the API can determine the user, their roles, the issuing authority, the intended audience and whether the token is still valid.
3. **Given** a wrong password, **When** a user attempts to sign in, **Then** access is refused with a generic failure.
4. **Given** an email that is not registered, **When** someone attempts to sign in, **Then** access is refused with a response indistinguishable from the wrong-password case.
5. **Given** a disabled user with correct credentials, **When** they attempt to sign in, **Then** access is refused with the same generic failure.

---

### User Story 2 - Role-based authorization across consumer APIs (Priority: P1)

Consumer APIs decide what a caller may do from the Administrator, Manager and Operator roles carried in the access token, without any lookup against the authentication service.

**Why this priority**: Role enforcement is the second half of "authentication and authorization" and must hold for every consumer API.

**Independent Test**: Issue tokens for one user per role. Call an Administrator-only operation with each and confirm only the Administrator is accepted.

**Acceptance Scenarios**:

1. **Given** a token for a user holding the Administrator role, **When** it is used on an Administrator-only operation, **Then** the operation is allowed.
2. **Given** a token for a user holding only Manager or Operator, **When** it is used on an Administrator-only operation, **Then** the operation is refused as forbidden.
3. **Given** a request with no token, an expired token, a tampered token, or a token from a different issuer or for a different audience, **When** it reaches a protected operation, **Then** it is rejected as unauthenticated.

---

### User Story 3 - Administrators manage users and roles (Priority: P2)

An Administrator lists users, views one user, creates users, enables or disables accounts, and assigns or removes roles. Only the three fixed roles can be assigned. Managers and Operators cannot do any of this.

**Why this priority**: Without administration, only the initial Administrator could ever exist. It depends on sign-in (P1) being in place.

**Independent Test**: As Administrator, create a user, assign a role, disable then re-enable the account, and try to assign a role named outside the fixed set. Repeat the administrative calls as a Manager and an Operator.

**Acceptance Scenarios**:

1. **Given** an authenticated Administrator, **When** they create a user with a unique email, an initial password and at least one supported role, **Then** the user exists, is identifiable by a stable unique identifier, and can sign in.
2. **Given** an existing email, **When** an Administrator creates another user with the same email, **Then** the request is refused.
3. **Given** an enabled user, **When** an Administrator disables them, **Then** the user can no longer obtain new authenticated access, and re-enabling restores it.
4. **Given** a user, **When** an Administrator assigns or removes Administrator, Manager or Operator, **Then** the user's roles change accordingly and appear in tokens issued afterwards.
5. **Given** a role name outside the three supported roles, **When** an Administrator attempts to assign it, **Then** the request is rejected and nothing changes.
6. **Given** a Manager or Operator, **When** they attempt any user-administration operation, **Then** it is refused as forbidden.
7. **Given** an Administrator, **When** they initiate a password reset for a user, **Then** a single-use, time-limited reset code is shown once to the Administrator, the user can redeem it to set a new password, all the user's sessions are revoked, and no one can ever read the old password.
8. **Given** a user who holds exactly one role, **When** an Administrator tries to remove that role, **Then** the request is refused; the Administrator may assign another role first or disable the user.
9. **Given** a user, **When** an Administrator assigns several supported roles, **Then** the user holds all of them and tokens carry all of them.
10. **Given** a disabled user holding a valid unused reset code, **When** they redeem it, **Then** it fails with the same generic failure as an invalid code and the user is not signed in; after an Administrator re-enables the account the user still has to sign in (or redeem) normally.

---

### User Story 4 - Renew a session and sign out (Priority: P2)

A signed-in user obtains a new short-lived access token without re-entering their password, using a refresh token. Each successful renewal replaces the refresh token, and the old one stops working. Signing out revokes the session.

**Why this priority**: Short-lived tokens are only usable if renewal exists; revocation limits the damage of stolen credentials.

**Independent Test**: Sign in, renew once, then try to reuse the first refresh token. Sign in again, sign out, then try to renew.

**Acceptance Scenarios**:

1. **Given** a valid refresh token, **When** the user requests renewal, **Then** they receive a new access token and a new refresh token, and the presented one becomes invalid.
2. **Given** a refresh token that has already been replaced, **When** it is presented again, **Then** the request is rejected and the whole session it belongs to is revoked, including its latest refresh token.
3. **Given** a user who signs out, **When** that session's refresh token is presented, **Then** it is rejected; the user's other sessions remain valid.
4. **Given** a disabled user with a previously valid refresh token, **When** they request renewal, **Then** it is refused.
5. **Given** a refresh token older than its individual lifetime (7 days by default), **When** it is presented, **Then** it is rejected.
6. **Given** a session that has reached its absolute lifetime (30 days from the original sign-in by default), **When** its latest refresh token is presented, **Then** it is rejected and the user must sign in with credentials; renewals before that point never extend the session past it.

---

### User Story 5 - View my own identity (Priority: P3)

An authenticated user retrieves their own identifier, email and roles, so the frontend can personalize and gate its screens.

**Why this priority**: Useful and simple, but the frontend can also read the token, so it is not blocking.

**Independent Test**: Sign in and request the current identity; confirm it contains only identifier, email and roles.

**Acceptance Scenarios**:

1. **Given** an authenticated user, **When** they request their identity, **Then** the response contains their identifier, email and assigned roles and nothing sensitive.
2. **Given** an unauthenticated caller, **When** they request an identity, **Then** the request is refused.

---

### User Story 6 - Establish the first Administrator on a clean install (Priority: P2)

An operator who has just installed the service establishes the first Administrator through a documented, secure procedure. Once an Administrator exists, this procedure no longer allows creating further administrators.

**Why this priority**: A clean installation has no users, so without this nothing can be administered.

**Independent Test**: Start from an empty data store, follow the documented procedure, and sign in as the new Administrator. Then confirm the procedure cannot create another Administrator.

**Acceptance Scenarios**:

1. **Given** a clean installation with no users, **When** the service starts with the initial Administrator email and password supplied through external configuration, **Then** exactly one Administrator is created and can sign in.
2. **Given** an installation that already contains any user, **When** the service starts with that configuration still present, **Then** it creates or elevates no account and ignores the configuration.
3. **Given** the procedure's required configuration is missing or invalid in production, **When** the service starts, **Then** it fails clearly rather than falling back to an insecure default.

---

### User Story 7 - Integrate a new consumer API and operate the service (Priority: P2)

A developer integrates a new consumer API using only the documentation. An operator or an agent with only Docker available can build, validate, migrate, run, back up and restore the service, and troubleshoot common sign-in failures.

**Why this priority**: The service is meant to grow beyond its first two consumers and be operated without tribal knowledge.

**Independent Test**: In an environment with only Docker, follow the documentation to build, test, migrate and run the service, then configure a sample consumer API for token validation and role authorization, without reading the service source.

**Acceptance Scenarios**:

1. **Given** only Docker, **When** a developer follows the documented commands, **Then** they can build, test, migrate and run the service.
2. **Given** the integration documentation, **When** a developer configures a new consumer API, **Then** it validates tokens and enforces roles without access to the authentication data store, passwords or private signing material.
3. **Given** a backup, **When** the documented restore procedure is followed, **Then** users, roles and state are recovered.

---

### User Story 8 - Read and try the API reference (Priority: P3)

Developers read reference documentation for sign-in, renewal, sign-out, current identity, user administration, role assignment, token usage and authorization expectations. In development they can execute requests from it; in production it is read-only.

**Why this priority**: Supports integration and adoption but does not affect runtime behavior.

**Independent Test**: Open the documentation in a development environment and execute a sign-in; open it in production and confirm requests cannot be executed.

**Acceptance Scenarios**:

1. **Given** a development environment, **When** a developer opens the documentation, **Then** they can sign in and run authenticated requests from it.
2. **Given** a production environment, **When** anyone opens the documentation, **Then** all operations are readable but none can be executed from it, and no secrets are shown.

---

### Edge Cases

- Sign-in attempts with a malformed email, empty password or missing fields: refused with a validation error that reveals nothing about account existence.
- Email comparison is case-insensitive, so `User@x.com` and `user@x.com` cannot both exist.
- A user's roles or enabled state change while they hold a valid access token: the existing token remains valid until it expires, within the short token lifetime, and new tokens reflect the change.
- Changes that would leave no enabled Administrator, or a user with no role: refused (FR-020).
- Presenting a previously replaced refresh token is treated as possible theft; the session it belongs to is revoked.
- A reset code that is expired, already used, never issued, or presented for a disabled account is rejected without revealing which.
- Signing-key compromise is handled by emergency replacement (FR-035), not by the overlap-based rotation of FR-010.
- Repeated failed sign-ins: throttled as defined in FR-021.
- A token-signing key rotation does not lock out users holding tokens signed by the previous key during the documented overlap window.
- The service is restarted or redeployed: users, roles and sessions persist.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST be the sole owner of user credentials, identity, account state and role assignments; consumer APIs MUST NOT hold passwords or password hashes.
- **FR-002**: The service MUST support exactly three roles, Administrator, Manager and Operator, and MUST NOT allow any other role to be created or assigned through the public API.
- **FR-003**: Users MUST be able to sign in with email and password; no separate username is required.
- **FR-004**: Each user MUST have a stable unique identifier, a unique email, an enabled/disabled state and a set of roles. A user MUST always hold at least one role, MAY hold several, and user creation MUST require at least one role.
- **FR-005**: Failed sign-ins MUST return an identical status code and response body whether the email is unknown, the password is wrong, the account is disabled or the account is temporarily refused (FR-021). For an unknown email the service MUST still perform password-verification work so processing time does not reveal existence; no tighter timing guarantee is made.
- **FR-006**: Disabled users MUST NOT obtain new access tokens or renew sessions.
- **FR-007**: Successful sign-in MUST return a signed access token carrying exactly the user identifier (`sub`), roles (`role`), issuer (`iss`), intended audience (`aud`), issue time (`iat`) and expiry (`exp`), and no other claims. The default lifetime is 15 minutes and MUST be configurable (1 to 60 minutes).
- **FR-008**: Access tokens MUST be verifiable by consumer APIs using only public verification information; consumer APIs MUST NOT need private signing material, database access or a per-request call to the service.
- **FR-009**: Tokens MUST be signed with an asymmetric key pair supplied to the service through external configuration (a certificate is permitted but not required). Private signing capability MUST be exclusive to the service and MUST NOT be exposed to consumer APIs, logs or documentation; consumer APIs MUST receive public verification material only.
- **FR-010**: The service MUST publish the public verification information, identified by a key identifier, and support key rotation with an overlap window, in a way consumer APIs can consume as documented. The service does not enforce certificate validity dates; it logs the expiry date at startup (FR-031). Consumer behavior when the key endpoint is unreachable is as clarified (keep last fetched keys; fail closed if none).
- **FR-011**: Authenticated users MUST be able to retrieve their own identifier, email and roles, and the response MUST NOT include passwords, hashes, refresh tokens, internal authentication data or signing material.
- **FR-012**: Administrators MUST be able to list users, retrieve a user, create users, enable and disable users, and assign and remove roles; Managers and Operators MUST be refused all of these operations.
- **FR-013**: Administrator-only operations MUST be enforced by the service itself, never trusting role information supplied by the client.
- **FR-014**: No user, Administrator or consumer API MUST be able to retrieve an existing password. Administrators MUST be able to initiate a password reset that issues a single-use, time-limited reset code (shown once, 24 hours by default, configurable between 1 and 72 hours) which the user redeems to choose a new password; the Administrator never learns the old or new password. Completing a reset MUST revoke all of the user's sessions. An Administrator MAY issue a reset code for a disabled account, but redeeming a code MUST fail while the account is disabled, with the same generic failure as an invalid, expired or used code (no account-state information is revealed); a failed redemption does not consume the code, and re-enabling the account neither signs the user in nor changes session or access-token rules. No email delivery is required.
- **FR-015**: Passwords MUST never appear in responses or logs. Any password set by an Administrator at user creation or by a user redeeming a reset code MUST be at least 8 characters and contain at least one upper-case letter, one lower-case letter and one digit; otherwise the request MUST be rejected with a validation error.
- **FR-016**: A signed-in session MUST be renewable using an opaque refresh token, yielding a new access token and a new refresh token without re-entering the password. Each refresh token has an individual lifetime of 7 days by default (configurable); renewal issues a new refresh token with a fresh individual lifetime, so the session is sliding, but a session MUST also have an absolute maximum lifetime of 30 days by default (configurable), counted from the original successful sign-in. Renewal MUST NOT extend a session beyond its absolute expiration (a refresh token never outlives its session), and once it is reached the user MUST sign in again with credentials. Both lifetimes MUST be validated at startup, and the absolute lifetime MUST be greater than or equal to the individual lifetime. Refresh tokens MUST be stored only in non-recoverable form.
- **FR-017**: Refresh tokens MUST be single-use: every successful renewal MUST invalidate the presented token. Presenting an already-replaced or revoked refresh token MUST be rejected and MUST revoke the entire session it belongs to.
- **FR-018**: Signing out MUST revoke only the current session. Disabling a user MUST revoke all of that user's sessions.
- **FR-019**: A clean installation MUST create the first Administrator from externally supplied configuration (initial email and password) at startup, only when the store contains no users. Once any user exists, this mechanism MUST create or modify nothing and its configuration MUST be ignored. Production MUST fail clearly if the initial password is missing when the store is empty, and the documentation MUST advise removing it after first start.
- **FR-020**: The service MUST NOT allow the last enabled Administrator to be disabled or stripped of the Administrator role, and MUST NOT allow a user's last remaining role to be removed.
- **FR-021**: The service MUST throttle repeated failed sign-in attempts: after 5 consecutive failures for an account, further attempts on that account are refused for 15 minutes with the same generic failure as FR-005; and each source address is limited to 10 requests per minute on the sign-in, renewal and reset-redemption endpoints, with excess requests receiving 429. All limits MUST be configurable.
- **FR-022**: Authentication-related errors MUST use a consistent, documented error format.
- **FR-023**: The service MUST publish reference documentation covering sign-in, renewal, sign-out, current identity, user administration, role assignment, token usage and authorization expectations.
- **FR-024**: In development the documentation MUST allow executing requests, including authenticated ones; in production it MUST be readable but MUST NOT allow executing requests or reveal secrets.
- **FR-025**: User data MUST persist across restarts and be changed only through documented, non-destructive upgrade steps; startup MUST NOT delete or reset existing data.
- **FR-026**: Production startup MUST fail clearly when mandatory security configuration (signing material, issuer, audience, initial Administrator settings where used) is missing or invalid.
- **FR-027**: The repository MUST document starting, configuring, initializing, persisting, upgrading the data schema, integrating a consumer API, configuring role authorization, backup and restore, and troubleshooting common sign-in failures.
- **FR-028**: Building, testing, migrating and running the service MUST be possible using only Docker, with no application technology stack installed locally.
- **FR-029**: A deliberately small automated suite MUST verify critical authentication, authorization, token/session and persistence behavior; no coverage percentage is required.
- **FR-030**: Adding another consumer API MUST require only documented configuration on the consumer side, with no change to how users authenticate.
- **FR-031**: The service MUST log security events without secrets: failed sign-ins (no email, no password), refresh-token reuse detection, user enable/disable, role assignment/removal, password-reset issuance and redemption, bootstrap outcome, and at startup the signing key identifier and certificate expiry date when a certificate is used. Passwords, access tokens, refresh tokens, reset codes and key material MUST never be logged.
- **FR-032**: Allowed frontend origins for cross-origin browser calls MUST be externally configured; in production no cross-origin access is allowed unless origins are configured.
- **FR-033**: The service MUST expose an anonymous health endpoint usable for container health and readiness checks that reveals no internal detail.
- **FR-034**: In production, private signing material (a PEM key or a PFX/PKCS#12 file) MUST be readable only by the authentication-service process and by the deployment or operator identity that provisions it; file ownership and permissions MUST prevent access by unrelated processes or users on the deployment host. It MUST be mounted read-only into the service container where practical, MUST NOT be baked into an image or committed to the repository, and MUST NOT be received by or accessible to any consumer API. When a PFX is used, its password MUST be supplied through external secret configuration and MUST NOT appear in source control, Dockerfiles, Compose files as a literal value, logs or generated documentation; a missing or invalid PFX password or file MUST make production startup fail with a clear message that contains no secret. PEM keys follow equivalent protection rules. Throwaway keys generated only for local validation, never used for real tokens, are exempt from the permission rule.
- **FR-035**: Suspected or confirmed signing-key compromise MUST be handled by emergency replacement, which differs from planned rotation. The compromised key MUST stop signing new tokens immediately (the service is restarted with a new key), and its public key MUST be removed from the published key set as soon as operationally possible, with no overlap period. Consumers using the key endpoint MUST replace (not merge) their cached key set on each refresh, so a removed key stops being accepted at their next refresh (at most 5 minutes later); consumers configured with static public keys MUST be updated manually or by deployment automation and may keep accepting tokens signed with the compromised key until then, a limitation the documentation MUST state explicitly. Invalidating trust in the old key takes priority over the validity of already-issued access tokens: operators MUST treat every access token signed with the compromised key as untrusted once consumers have received the updated verification material. Refresh-token and session records are not destroyed automatically unless a separate compromise affects them. No distributed revocation service or token-introspection protocol is introduced.

### Key Entities

- **User**: A person who can sign in. Has a stable identifier, unique email, enabled state, assigned roles and a protected credential that is never readable.
- **Role**: One of exactly Administrator, Manager, Operator. A fixed concept, not user-creatable.
- **Access Token**: Short-lived signed proof of identity and roles. Carries user identifier, roles, issuer, audience, issue time and expiry.
- **Refresh Token / Session**: A revocable, single-use credential tied to a signed-in session, replaced on each renewal and revoked on sign-out or detected reuse. A session has an absolute maximum lifetime counted from the original sign-in.
- **Signing Key Pair**: The private key held only by the service and the public verification information shared with consumer APIs.
- **Consumer API**: An independent business API that trusts tokens from this service by validating them locally and authorizing by role.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A valid enabled user can sign in with email and password and receive a usable access token in under 2 seconds under normal load.
- **SC-002**: 100% of sign-in attempts with invalid credentials, unknown emails or disabled accounts are refused, with identical status code and body (FR-005).
- **SC-003**: An independent consumer API configured with only public verification information accepts 100% of valid tokens and rejects 100% of expired, tampered, wrong-issuer or wrong-audience tokens, with no access to the authentication data store.
- **SC-004**: In every tested case, Administrator-only operations accept Administrators and refuse Managers and Operators.
- **SC-005**: Assigning any role outside Administrator, Manager and Operator is rejected in 100% of attempts.
- **SC-006**: A replaced or signed-out refresh token is rejected in 100% of reuse attempts, and a user can obtain a new access token through renewal without re-entering their password.
- **SC-007**: A clean installation can have its first Administrator established by following the documentation in under 15 minutes, and the procedure cannot create further administrators afterwards.
- **SC-008**: Production API documentation is fully readable and allows 0 requests to be executed from it.
- **SC-009**: A person with only Docker can build, test, migrate and run the service by following the documentation, with no local installation of the application technology stack.
- **SC-010**: A minimal automated suite covering sign-in, role enforcement, token validation, session renewal and persistence passes in the Docker environment.
- **SC-011**: A developer can integrate a new consumer API using the documentation alone, without inspecting the service's implementation.
- **SC-012**: After a restore from backup, 100% of users, roles and enabled states are recovered and sign-in works.

## Assumptions

- The initial deployment has two consumer APIs and one shared frontend; more consumer APIs are expected over time and are all operated within the same trust domain.
- Self-registration, social/third-party sign-in, multi-factor authentication and email-delivered password recovery are out of scope; the only password reset is administrator-initiated.
- Role changes and account disablement reach consumer APIs when the current access token expires, not instantly; this lag is acceptable.
- OAuth/OIDC, federation and external identity providers are out of scope; the service issues its own tokens only.
- Authentication data is stored on a single node with durable storage; high availability and horizontal scaling are out of scope for this feature.
- Production traffic reaches the service through a TLS-terminating reverse proxy; the service itself speaks HTTP behind it.
- Administrators are trusted accounts; an Administrator who issues a reset code can take over that user's account.
- The architecture, technology and testing constraints in the project constitution apply to the plan and implementation, not to this specification.
