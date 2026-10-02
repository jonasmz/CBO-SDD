# Authorization

The complete, exact, case-sensitive role set is:

- `Administrator`
- `Manager`
- `Operator`

Arbitrary role creation is not supported. The authentication service defines and propagates these roles;
each consumer API independently decides what Manager and Operator may do in its own domain.

The auth service applies the `RequireAdministrator` policy to all `/api/users` operations. Missing/invalid
authentication returns 401; authenticated Manager and Operator callers receive 403. Clients cannot create
new role names or turn untrusted input into claims—creation and role-change requests are checked against
the canonical role set, and access-token roles come from persisted Identity memberships.

Each user must retain at least one role. Removing the last role is refused. Disabling the last enabled
Administrator, or removing that Administrator membership, is also refused. These invariants are enforced
in application handlers, not only in UI code.

An Administrator who issues a password-reset code can take over the target account; treat Administrator
access accordingly and audit reset issuance. Role changes and disabling revoke renewable sessions where
specified, but already-issued access tokens remain valid until their short expiry (plus consumer clock
skew). That bounded lag is accepted; consumer APIs do not query the auth database per request.
