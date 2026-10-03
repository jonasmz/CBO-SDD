# JWT consumer integration

Consumers need only the issuer, audience, and public verification keys. They do not need the auth database,
password data, private signing material, refresh tokens, or a per-request call to this service.

## ASP.NET Core configuration

Fetch `/.well-known/jwks.json` at startup into an `IJwksKeyCache`. The cache must expose the latest complete
set as `IEnumerable<SecurityKey>` and resolve by `kid`.

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

builder.Services.AddSingleton<IJwksKeyCache, JwksKeyCache>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IJwksKeyCache>((options, cache) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Auth:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Auth:Audience"],
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "sub",
            RoleClaimType = "role",
            IssuerSigningKeyResolver = (_, _, kid, _) => cache.Resolve(kid)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Administrator", p => p.RequireRole("Administrator"));
    options.AddPolicy("Manager", p => p.RequireRole("Manager"));
    options.AddPolicy("Operator", p => p.RequireRole("Operator"));
});
```

`JwksKeyCache.Resolve(kid)` returns only the matching public key and implements the refresh rules below. A
static public PEM/certificate can instead be loaded as `IssuerSigningKey`; it must contain public material
only.

## Required cache behavior

- Fetch JWKS at startup. With no successfully fetched keys, reject every token.
- Treat the cached set as fresh for at most five minutes; re-fetch at or before that age.
- Resolve the JWT header `kid`. An unknown `kid` may force an immediate refresh, but at most once per minute
  per process. Reject if it remains unknown.
- A successful refresh replaces the whole cached set. Never merge it with old keys.
- If refresh fails, the consumer may continue using its last successfully fetched set during the outage.

Tokens use RS256 and contain only `sub`, one or more `role` claims, `iss`, `aud`, `iat`, and `exp`. Refresh
tokens are never sent to consumer APIs.

## Rotation and emergency replacement

For planned rotation, publish the old public key as previous while signing with the new private key; after
one access-token lifetime plus skew, remove the old public key. Consumers select by `kid`.

For emergency replacement, stop signing with the compromised key, remove its public key immediately (no
overlap), start with the replacement, and make consumers refresh authoritative JWKS. After a successful
refresh, replacement—not merging—removes trust in the old key and all tokens signed by it are untrusted.

The five-minute removal expectation depends on successful JWKS retrieval. If a consumer cannot retrieve the
updated authoritative key set, there is no bounded guarantee that a compromised cached key disappears
within five minutes. Static-key consumers keep accepting the compromised key until their file is updated by
an operator or deployment automation.
