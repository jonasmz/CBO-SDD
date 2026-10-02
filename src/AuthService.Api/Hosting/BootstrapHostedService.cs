using AuthService.Application.Features.Bootstrap.BootstrapAdministrator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthService.Api.Hosting;

public sealed partial class BootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IOptions<BootstrapOptions> options,
    ILogger<BootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BootstrapAdministratorHandler>();
        var configuration = options.Value;
        var outcome = await handler.HandleAsync(
            new BootstrapAdministratorCommand(configuration.AdminEmail, configuration.AdminPassword),
            cancellationToken);

        switch (outcome)
        {
            case BootstrapAdministratorOutcome.Created:
                Log.BootstrapAdministratorCreated(logger);
                break;
            case BootstrapAdministratorOutcome.SkippedUsersExist:
                Log.BootstrapSkippedUsersExist(logger);
                break;
            case BootstrapAdministratorOutcome.SkippedNotConfigured:
                Log.BootstrapSkippedNotConfigured(logger);
                break;
            case BootstrapAdministratorOutcome.InvalidConfiguration:
                Log.BootstrapSkippedInvalidConfiguration(logger);
                break;
        }

        if (!environment.IsDevelopment() && outcome is BootstrapAdministratorOutcome.SkippedNotConfigured or BootstrapAdministratorOutcome.InvalidConfiguration)
        {
            throw new InvalidOperationException("Bootstrap Administrator configuration is missing or invalid for an empty user store.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Bootstrap administrator created")]
        public static partial void BootstrapAdministratorCreated(ILogger logger);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Bootstrap skipped: users already exist")]
        public static partial void BootstrapSkippedUsersExist(ILogger logger);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Bootstrap skipped: not configured")]
        public static partial void BootstrapSkippedNotConfigured(ILogger logger);

        [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Bootstrap skipped: invalid configuration")]
        public static partial void BootstrapSkippedInvalidConfiguration(ILogger logger);
    }
}
