using AuthService.Application.Features.Bootstrap.BootstrapAdministrator;
using AuthService.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests;

public sealed class BootstrapTests : IClassFixture<AuthServiceFactory>
{
    private readonly AuthServiceFactory _factory;
    public BootstrapTests(AuthServiceFactory factory) => _factory = factory;

    [Fact]
    public async Task Bootstrap_creates_administrator_only_for_empty_store()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BootstrapAdministratorHandler>();
        Assert.Equal(BootstrapAdministratorOutcome.Created, await handler.HandleAsync(new BootstrapAdministratorCommand("bootstrap@example.test", "ValidPass1")));
        var user = await _factory.FindUserAsync((await scope.ServiceProvider.GetRequiredService<AuthService.Application.Abstractions.IUserDirectory>().FindByEmailAsync("bootstrap@example.test"))!.Id);
        Assert.NotNull(user);
        Assert.Equal(BootstrapAdministratorOutcome.SkippedUsersExist, await handler.HandleAsync(new BootstrapAdministratorCommand("other@example.test", "ValidPass1")));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<AuthService.Application.Abstractions.IUserDirectory>().FindByEmailAsync("other@example.test"));
    }
}
