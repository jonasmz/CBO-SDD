using System.Net;
using System.Net.Http.Headers;
using AuthService.Domain.Roles;

namespace AuthService.IntegrationTests;

public sealed class AuthorizationTests : IClassFixture<AuthServiceFactory>
{
    private readonly AuthServiceFactory _factory;
    public AuthorizationTests(AuthServiceFactory factory) => _factory = factory;

    [Fact]
    public async Task Administrator_can_list_users()
    {
        var user = await _factory.CreateUserAsync(AppRoles.Administrator);
        var (client, token, _) = await _factory.LoginAsync(user);
        using (client) { client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode); }
    }

    [Theory]
    [InlineData(AppRoles.Manager)]
    [InlineData(AppRoles.Operator)]
    public async Task Non_administrators_are_forbidden(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        var (client, token, _) = await _factory.LoginAsync(user);
        using (client) { client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode); }
    }

    [Fact]
    public async Task Unauthenticated_request_is_challenged() => Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/users")).StatusCode);
}
