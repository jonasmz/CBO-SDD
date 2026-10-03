using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthService.Domain.Roles;

namespace AuthService.IntegrationTests;

public sealed class RoleAssignmentTests
{
    [Fact]
    public async Task Supported_role_is_assigned_and_unsupported_role_changes_nothing()
    {
        using var factory = new AuthServiceFactory();
        var admin = await factory.CreateUserAsync(AppRoles.Administrator);
        var target = await factory.CreateUserAsync(AppRoles.Manager);
        var (client, token, _) = await factory.LoginAsync(admin);
        using (client)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/api/users/{target.Id}/roles/{AppRoles.Operator}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync($"/api/users/{target.Id}/roles/Unsupported", null)).StatusCode);
        }
        using var check = factory.CreateClient();
        check.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var user = await check.GetFromJsonAsync<UserView>($"/api/users/{target.Id}");
        Assert.NotNull(user); Assert.Contains(AppRoles.Operator, user.Roles); Assert.DoesNotContain("Unsupported", user.Roles);
    }

    [Fact]
    public async Task Last_role_and_last_enabled_administrator_are_protected()
    {
        using var factory = new AuthServiceFactory();
        var admin = await factory.CreateUserAsync(AppRoles.Administrator);
        var target = await factory.CreateUserAsync(AppRoles.Manager);
        var (client, token, _) = await factory.LoginAsync(admin);
        using (client)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/users/{target.Id}/roles/{AppRoles.Manager}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/users/{admin.Id}/disable", null)).StatusCode);
        }
    }

    private sealed record UserView(Guid Id, IReadOnlyList<string> Roles);
}
