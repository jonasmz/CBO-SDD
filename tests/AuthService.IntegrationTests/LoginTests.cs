using System.Net;
using System.Net.Http.Json;

namespace AuthService.IntegrationTests;

public sealed class LoginTests : IClassFixture<AuthServiceFactory>
{
    private readonly AuthServiceFactory _factory;
    public LoginTests(AuthServiceFactory factory) => _factory = factory;

    [Fact]
    public async Task Valid_credentials_return_an_access_token()
    {
        var user = await _factory.CreateUserAsync(AuthService.Domain.Roles.AppRoles.Administrator);
        var (_, accessToken, _) = await _factory.LoginAsync(user);
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_have_the_same_generic_response()
    {
        var user = await _factory.CreateUserAsync(AuthService.Domain.Roles.AppRoles.Manager);
        using var client = _factory.CreateClient();
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, Password = "WrongPass1" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { Email = "unknown@example.test", Password = "WrongPass1" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        var wrongProblem = await wrong.Content.ReadFromJsonAsync<ProblemView>();
        var unknownProblem = await unknown.Content.ReadFromJsonAsync<ProblemView>();
        Assert.Equal(wrongProblem, unknownProblem);
    }

    [Fact]
    public async Task Disabled_user_cannot_sign_in()
    {
        var user = await _factory.CreateUserAsync(AuthService.Domain.Roles.AppRoles.Operator, enabled: false);
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, user.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record ProblemView(int Status, string Title);
}
