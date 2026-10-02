# Architecture

## Layers and dependency direction

```text
HTTP client
    |
    v
AuthService.Api  ---- composition, HTTP, authentication/authorization middleware
    |       \
    v        v
Application  Infrastructure ---- Identity, EF Core/SQLite, JWT, key loading
    |
    v
Domain ---- roles, password rules, refresh-token state transitions
```

Dependencies point inward:

- Domain references no other project or framework.
- Application references Domain and declares use cases plus narrow ports.
- Infrastructure references Application and Domain to implement those ports.
- API references Application and Infrastructure and is the composition root.

Architecture tests enforce that Domain does not reference Infrastructure, ASP.NET Core, EF Core, or
Identity, and Application does not reference Infrastructure or API.

## Vertical slices and responsibilities

Application behavior is grouped by capability under `Features/Authentication`, `Features/Identity`,
`Features/Users`, and `Features/Bootstrap`. Plain handlers implement use cases; no mediator, generic
repository, custom unit of work, or event bus is present.

Business decisions stay in Domain or Application: supported roles, password rules, generic login failure,
disabled-user behavior, bootstrap-only-on-empty-store, role invariants, and session/reuse decisions.
Infrastructure performs mechanics: Identity queries and password verification, EF persistence and
transactions, token hashing, RSA signing, and key loading. API maps requests/results, applies policies,
and wires middleware without making business decisions.

## Request flow

```text
Request -> ASP.NET middleware -> endpoint adapter -> application handler
                                             |-> narrow port -> infrastructure adapter
                                             |                  |-> Identity / SQLite / RSA
                                             '<- outcome/facts <-'
        <- explicit response contract <- endpoint adapter
```

Consumers validate access tokens independently using public material from JWKS or a static public key.
They never receive private keys, password data, database access, or a requirement to call this service on
every request.
