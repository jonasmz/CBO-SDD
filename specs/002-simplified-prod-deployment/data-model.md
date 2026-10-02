# Data Model: Simplified Production Deployment

No database schema or migration changes. Persistent state layout (volume `authservice-data` mounted at `/data`):

| Item | Path | Owner/mode | Lifecycle |
|---|---|---|---|
| SQLite database | `/data/auth.db` | runtime UID | Created/migrated at start |
| Data Protection key ring | `/data/keys/` | runtime UID | Created by ASP.NET Core |
| Generated signing key | `/data/signing/signing.pem` (dir 0700, file 0600) | runtime UID | Created once on first provisioning; never rewritten |

## Signing source state machine (startup)

```text
explicit PEM and PFX both set            → FAIL (conflict)
PFX path w/o password or password w/o path → FAIL (partial)
explicit PEM or PFX valid                → use it; auto mode off
explicit configured but missing/invalid  → FAIL
no explicit source:
  key file valid                         → load
  key file present but invalid           → FAIL (never rewrite)
  key file absent, no users              → generate + persist
  key file absent, users exist           → FAIL unless AllowKeyRegeneration=true → generate (new kid, warning)
```

Deleting the volume resets the installation entirely (database, ring, key); documented as a key replacement event.
