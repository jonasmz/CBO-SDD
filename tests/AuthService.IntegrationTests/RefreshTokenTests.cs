using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthService.Domain.Roles;

namespace AuthService.IntegrationTests;

public sealed class RefreshTokenTests
{
    [Fact]
    public async Task Rotation_replaces_token_and_reuse_revokes_the_session()
    {
        using var factory = new AuthServiceFactory();
        var user = await factory.CreateUserAsync(AppRoles.Manager);
        var (client, _, refresh) = await factory.LoginAsync(user);
        using (client)
        {
            var renewed = await Renew(client, refresh);
            Assert.Equal(HttpStatusCode.OK, renewed.Response.StatusCode);
            Assert.NotEqual(refresh, renewed.Payload.RefreshToken);
            Assert.False(string.IsNullOrWhiteSpace(renewed.Payload.AccessToken));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Renew(client, refresh)).Response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Renew(client, renewed.Payload.RefreshToken)).Response.StatusCode);
        }
    }

    [Fact]
    public async Task Logout_is_idempotent_and_revokes_the_session()
    {
        using var factory = new AuthServiceFactory();
        var user = await factory.CreateUserAsync(AppRoles.Operator);
        var (client, _, refresh) = await factory.LoginAsync(user);
        using (client)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = refresh })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = refresh })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Renew(client, refresh)).Response.StatusCode);
        }
    }

    [Fact]
    public async Task Password_reset_revokes_sessions_and_disabled_user_does_not_consume_code()
    {
        using var factory = new AuthServiceFactory();
        var admin = await factory.CreateUserAsync(AppRoles.Administrator);
        var target = await factory.CreateUserAsync(AppRoles.Manager);
        var (targetClient, _, refresh) = await factory.LoginAsync(target);
        var (adminClient, adminToken, _) = await factory.LoginAsync(admin);
        using (targetClient)
        using (adminClient)
        {
            adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var issued = await adminClient.PostAsync($"/api/users/{target.Id}/password-reset", null);
            var code = (await issued.Content.ReadFromJsonAsync<ResetCode>())!.resetCode;
            var reset = await adminClient.PostAsJsonAsync("/api/auth/reset-password", new { target.Email, resetCode = code, newPassword = "ChangedPass1" });
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Renew(targetClient, refresh)).Response.StatusCode);

            var secondCode = (await (await adminClient.PostAsync($"/api/users/{target.Id}/password-reset", null)).Content.ReadFromJsonAsync<ResetCode>())!.resetCode;
            Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsync($"/api/users/{target.Id}/disable", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await adminClient.PostAsJsonAsync("/api/auth/reset-password", new { target.Email, resetCode = secondCode, newPassword = "AnotherPass1" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsync($"/api/users/{target.Id}/enable", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsJsonAsync("/api/auth/reset-password", new { target.Email, resetCode = secondCode, newPassword = "AnotherPass1" })).StatusCode);
        }
    }

    [Fact]
    public async Task Absolute_session_expiration_wins_and_rotation_never_extends_it()
    {
        using var factory = new AuthServiceFactory();
        var user = await factory.CreateUserAsync(AppRoles.Manager);
        var (client, _, refresh) = await factory.LoginAsync(user);
        using (client)
        {
            var sessionExpiry = factory.Time.GetUtcNow().AddDays(2);
            factory.Time.Advance(TimeSpan.FromHours(18));
            var first = await Renew(client, refresh);
            Assert.Equal(HttpStatusCode.OK, first.Response.StatusCode);
            factory.Time.Advance(TimeSpan.FromHours(17));
            var rotated = await Renew(client, first.Payload.RefreshToken);
            Assert.Equal(HttpStatusCode.OK, rotated.Response.StatusCode);
            Assert.Equal(sessionExpiry, rotated.Payload.RefreshTokenExpiresAt);
            factory.Time.Advance(TimeSpan.FromHours(13));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Renew(client, refresh)).Response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Renew(client, rotated.Payload.RefreshToken)).Response.StatusCode);
        }
    }

    private static async Task<(HttpResponseMessage Response, TokenResponse Payload)> Renew(HttpClient client, string refreshToken)
    {
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        var payload = response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<TokenResponse>())! : new TokenResponse("", 0, "", default);
        return (response, payload);
    }

    private sealed record ResetCode(string resetCode, DateTimeOffset expiresAt);
}
