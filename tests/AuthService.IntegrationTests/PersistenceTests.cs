using AuthService.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests;

public sealed class PersistenceTests : IClassFixture<AuthServiceFactory>
{
    private readonly AuthServiceFactory _factory;
    public PersistenceTests(AuthServiceFactory factory) => _factory = factory;

    [Fact]
    public async Task Identity_roles_and_refresh_session_survive_host_restart()
    {
        var user = await _factory.CreateUserAsync(AppRoles.Manager);
        var (client, _, _) = await _factory.LoginAsync(user);
        client.Dispose();
        using var restarted = _factory.Restart();
        _ = restarted.CreateClient();
        var persisted = await restarted.FindUserAsync(user.Id);
        Assert.NotNull(persisted);
        Assert.Equal(user.Email, persisted.Email);
        await using var scope = restarted.Services.CreateAsyncScope();
        var roles = await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AuthService.Infrastructure.Identity.ApplicationUser>>().GetRolesAsync(persisted);
        Assert.Contains(AppRoles.Manager, roles);
        Assert.Equal(1, await restarted.RefreshTokenCountAsync(user.Id));
    }
}
