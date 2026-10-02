<!--
Sync Impact Report (temporary; remove before commit)
- Version change: (unratified template) → 1.0.0
- Modified principles: none renamed (initial adoption; all template placeholders replaced)
- Added sections: Core Principles I–XII, Technology & Operational Constraints,
  Development Workflow & Quality Gates, Governance
- Removed sections: none
- Deferred TODOs: none
-->
# Auth API Constitution

This constitution governs a standalone authentication and authorization REST API: the central
identity and authorization service for a shared frontend and multiple independent REST business
APIs. It is authoritative over feature specifications, plans, tasks and implementation.

## Core Principles

### I. Hexagonal Architecture with Vertical Slices (NON-NEGOTIABLE)

The system MUST be organized into four boundaries: Domain, Application, Infrastructure, API.
Dependencies MUST point inward.

- **Domain** MUST be technology agnostic. It MUST NOT depend on ASP.NET Core, ASP.NET Core
  Identity, Entity Framework Core, SQLite, JWT libraries, HTTP, Scalar, OpenAPI, Docker, or any
  infrastructure-specific library.
- **Application** MAY depend on Domain. It defines use cases and the ports/contracts needed from
  external systems. It MUST NOT depend on Infrastructure or API.
- **Infrastructure** implements Application ports and holds framework-specific concerns.
- **API** is the HTTP adapter and the application composition root.
- Business rules MUST NOT live in controllers, endpoint handlers, EF Core configurations,
  Identity persistence entities, middleware, or infrastructure adapters.
- Functionality MUST be organized by business capability and use case (expected areas:
  Authentication, Current Identity, Users, Roles), not by large technical service classes.
  A slice MAY contain request/response contracts, commands or queries, handlers, validation,
  endpoint mapping and feature-specific abstractions. Shared abstractions MUST be introduced only
  when genuinely required by more than one feature. Identical boilerplate MUST NOT be forced on
  every feature.

Rationale: keeps business rules independent of frameworks and keeps features cohesive and
independently changeable.

### II. Simple, Explicit Design

SOLID MUST be applied, with particular emphasis on Single Responsibility, Dependency Inversion
and Interface Segregation. Simple, explicit implementations are preferred. Design patterns MUST
solve a concrete architectural or domain problem. The following MUST NOT be introduced without
demonstrated need: MediatR, generic repository frameworks, custom Unit of Work abstractions over
EF Core, event buses, domain-event infrastructure, distributed messaging, service locators,
unnecessary inheritance hierarchies. Command/query terminology MAY be used without requiring
CQRS or mediator frameworks.

Rationale: speculative abstraction costs more than it saves in a small, security-critical service.

### III. Centralized Authentication (NON-NEGOTIABLE)

Authentication is centralized in this service. Users authenticate with email and password; email
is the user-facing login identifier and a separate username MUST NOT be required. Passwords MUST
be managed through ASP.NET Core Identity; custom password hashing MUST NOT be implemented.
Passwords MUST never be logged, returned by APIs, stored in plaintext, or exposed to consumer
APIs. Authentication failures MUST NOT reveal whether an email exists, whether the password was
incorrect, or whether another credential component failed.

### IV. Fixed Role-Based Authorization (NON-NEGOTIABLE)

Authorization is role based. The complete role set is exactly: Administrator, Manager, Operator.
Arbitrary roles MUST NOT be creatable through the public API. Role names MUST have a single
canonical definition and MUST NOT be duplicated as string literals across the implementation.
Authorization MUST always be enforced server-side. Role information supplied by clients MUST NOT
be trusted when creating or modifying users. Consumer APIs authorize using role claims in access
tokens. Role-restricted operations MUST use ASP.NET Core authorization policies (or an equivalent
framework mechanism), not scattered manual role checks.

### V. Token Security (NON-NEGOTIABLE)

This service is the token authority and issues JWT access tokens. Consumer APIs MUST be able to
validate tokens without access to the auth database, without password data, and without querying
this service on every request. JWT signing MUST use asymmetric cryptography. Only this service
may possess private signing material; consumer APIs receive verification/public material only.
The production key or certificate mechanism MUST be configurable without source-code changes.
Private signing material MUST NOT be committed, embedded in source, logged, or distributed to
consumer APIs. Access tokens MUST contain only claims required for identity and authorization.

### VI. Persistence as Infrastructure

Persistence is an infrastructure concern; database-specific types MUST NOT leak into Domain.
Schema evolution MUST use EF Core migrations. Production startup MUST NOT silently delete or
recreate the database, reset data, or overwrite user information.

### VII. Explicit API Contracts

The API MUST use explicit request and response contracts; ASP.NET Core Identity persistence
entities MUST NOT be exposed as API contracts. HTTP semantics MUST be correct, errors SHOULD use
Problem Details where appropriate, and protected operations MUST require authentication.

### VIII. OpenAPI and Scalar Documentation

The API MUST expose OpenAPI documentation with Scalar as the reference UI.

- Development: full documentation, interactive request execution enabled, JWT authentication
  usable through Scalar.
- Production: documentation remains readable, interactive request execution MUST be disabled,
  documentation MUST be effectively read-only, and secrets or sensitive implementation details
  MUST NOT be exposed.

If the selected Scalar version cannot guarantee this exact behavior, an equivalent
production-safe configuration preserving readable docs without request execution MUST be used.

### IX. Container-First Development (NON-NEGOTIABLE)

The AI implementation agent runs in a generic Docker container with access to the host Docker
daemon. It MUST NOT install into its own container the .NET SDK, ASP.NET tooling, EF Core
tooling, SQLite development tooling, or other project-specific runtime dependencies. The project
MUST provide dedicated Docker development containers with the required stack, and ALL
technology-specific operations MUST run through Docker, including `dotnet restore`, `build`,
`test`, `run`, `dotnet ef migrations add`, `dotnet ef database update`, and application
execution. The environment MUST be reproducible entirely from repository-controlled Docker
configuration; a developer or agent with Docker MUST NOT need a local .NET installation.

### X. Docker Reproducibility

The repository MUST contain everything required to reproduce development and runtime
environments, at minimum: Dockerfile, development Docker configuration where needed, Docker
Compose configuration, `.dockerignore`, persistent SQLite storage configuration, and documented
execution commands. Supported .NET 10 SDK/runtime images MUST be used. The production runtime
image SHOULD use the ASP.NET Core runtime image, not the SDK. The project MUST NOT depend on
undocumented manual configuration of the agent or host.

### XI. Minimal, Risk-Based Testing

Testing is intentionally minimal. There MUST NOT be a code-coverage-percentage requirement. The
goal is the smallest practical suite that detects failures in critical behavior: authentication,
authorization, JWT issuance and validation, role enforcement, refresh-token lifecycle, disabled
accounts, and critical persistence integration. Integration tests MUST be preferred where
behavior depends substantially on ASP.NET Core Identity, EF Core, SQLite, or authentication and
authorization middleware. Tests MUST NOT be written merely to raise counts or coverage, nor for
trivial DTOs, getters/setters, constants, framework-guaranteed behavior, trivial DI
registrations, or exhaustive low-value combinations. A few high-value integration tests are
preferred over many low-value unit tests.

**Minimum Validation Gate** — automated validation MUST demonstrate that:

1. valid credentials can authenticate;
2. invalid credentials are rejected;
3. a disabled user cannot obtain new authentication;
4. a JWT issued by the service can be independently validated;
5. expected role claims are present;
6. Administrator authorization succeeds where required;
7. Manager and Operator are rejected from Administrator-only operations;
8. unsupported roles cannot be assigned;
9. refresh-token rotation works, if refresh tokens are implemented;
10. critical EF Core + SQLite persistence works with the real provider;
11. the project builds and tests successfully inside the Docker development environment.

### XII. Configuration, Secrets and Maintainability

Standard ASP.NET Core configuration mechanisms MUST be used. Security-sensitive configuration
MUST be externally configurable and secrets MUST NOT be committed. Production startup MUST fail
clearly if mandatory security configuration is absent or invalid, and development convenience
MUST NOT weaken production defaults. Nullable reference types MUST be enabled; compiler/analyzer
findings affecting correctness or security MUST be resolved. Dependencies MUST be explicit,
Dependency Injection MUST be used, and classes and methods SHOULD have focused responsibilities.

## Technology & Operational Constraints

- Stack: .NET 10, ASP.NET Core, ASP.NET Core Identity, EF Core with SQLite, JWT, OpenAPI, Scalar,
  Docker and Docker Compose.
- The repository MUST document: architecture; Docker-based development; configuration;
  authentication flow; authorization; JWT validation; consumer API integration; EF Core
  migrations; SQLite persistence; initial Administrator provisioning; production deployment;
  signing-key/certificate management; backup and restore; troubleshooting; operational
  procedures.
- Development documentation MUST assume .NET is NOT installed on the agent or developer machine.

## Development Workflow & Quality Gates

- Every plan MUST include a Constitution Check against Principles I–XII before design and after.
- Every change MUST build and pass tests inside the Docker development environment.
- Reviews MUST verify layer dependency direction, absence of business rules in adapters, absence
  of secrets in source control, and that the Minimum Validation Gate remains satisfied.
- Added abstractions or dependencies from the "not without demonstrated need" list in Principle II
  MUST be justified in writing in the plan.

## Governance

This constitution supersedes all other practices and is authoritative over specifications, plans,
tasks and implementation. Amendments MUST be documented, include rationale and any needed
migration of affected artifacts, and be committed with an updated version and amendment date.
Versioning follows semantic versioning: MAJOR for backward-incompatible removal or redefinition
of principles; MINOR for new principles/sections or materially expanded guidance; PATCH for
clarifications and wording fixes. Compliance MUST be reviewed in every plan and code review;
violations MUST be fixed or the constitution formally amended, never silently bypassed.

**Version**: 1.0.0 | **Ratified**: 2026-10-02 | **Last Amended**: 2026-10-02
