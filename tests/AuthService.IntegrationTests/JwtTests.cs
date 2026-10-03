using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using AuthService.Domain.Roles;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.IntegrationTests;

public sealed class JwtTests : IClassFixture<AuthServiceFactory>
{
    private readonly AuthServiceFactory _factory;
    public JwtTests(AuthServiceFactory factory) => _factory = factory;

    [Fact]
    public async Task Issued_token_is_RS256_and_validates_with_only_JWKS_public_material()
    {
        var user = await _factory.CreateUserAsync(AppRoles.Administrator);
        var (client, token, _) = await _factory.LoginAsync(user);
        using (client)
        {
            var jwks = JsonDocument.Parse(await client.GetStringAsync("/.well-known/jwks.json"));
            var key = new JsonWebKey(jwks.RootElement.GetProperty("keys")[0].GetRawText());
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = key,
                ValidateIssuer = true, ValidIssuer = "https://issuer.integration.test",
                ValidateAudience = true, ValidAudience = "integration-consumer",
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                NameClaimType = "sub", RoleClaimType = "role"
            }, out var validated);
            var jwt = Assert.IsType<JwtSecurityToken>(validated);
            Assert.Equal(SecurityAlgorithms.RsaSha256, jwt.Header.Alg);
            Assert.False(string.IsNullOrWhiteSpace(jwt.Header.Kid));
            Assert.Equal(user.Id.ToString(), principal.FindFirstValue("sub"));
            Assert.Contains(AppRoles.Administrator, principal.FindAll("role").Select(claim => claim.Value));
            Assert.Equal(new[] { "aud", "exp", "iat", "iss", "role", "sub" }, jwt.Claims.Select(claim => claim.Type).Distinct().OrderBy(type => type));
        }
    }

    [Theory]
    [InlineData("wrong-issuer", "integration-consumer")]
    [InlineData("https://issuer.integration.test", "wrong-audience")]
    public async Task Consumer_validation_rejects_wrong_issuer_or_audience(string issuer, string audience)
    {
        var user = await _factory.CreateUserAsync(AppRoles.Manager);
        var (client, token, _) = await _factory.LoginAsync(user);
        using (client)
        {
            var document = JsonDocument.Parse(await client.GetStringAsync("/.well-known/jwks.json"));
            var key = new JsonWebKey(document.RootElement.GetProperty("keys")[0].GetRawText());
            Assert.ThrowsAny<SecurityTokenValidationException>(() => new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience, ValidateLifetime = true }, out _));
        }
    }
}
