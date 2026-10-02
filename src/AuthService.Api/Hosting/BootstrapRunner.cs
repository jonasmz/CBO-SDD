using AuthService.Application.Features.Bootstrap.BootstrapAdministrator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthService.Api.Hosting;

/// <summary>
/// Creates the first Administrator on an empty user store. Invoked inline from <c>Program.cs</c> before the
/// server starts, so startup ordering never depends on hosted-service/Kestrel start order.
/// </summary>
public sealed partial class BootstrapRunner(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IOptions<BootstrapOptions> options,
    ILogger<BootstrapRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BootstrapAdministratorHandler>();
        var configuration = options.Value;
        // The handler checks for existing users first: when users exist the configured values are never validated or used.
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
            throw new InvalidOperationException(
                "Bootstrap Administrator configuration is missing or invalid for an empty user store: "
                + "Bootstrap__AdminEmail and Bootstrap__AdminPassword must both be set, with a valid email address "
                + "and a password that satisfies the password policy.");
        }
    }

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
