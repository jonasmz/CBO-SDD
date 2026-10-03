using AuthService.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Infrastructure.Persistence;

/// <summary>
/// Single implementation of database initialization shared by normal startup and the
/// explicit <c>--migrate</c> command. The two entry points differ only in whether the
/// schema is migrated (<see cref="MigrateAsync"/>) or merely verified (<see cref="EnsureCurrentAsync"/>).
/// </summary>
public sealed class DatabaseInitializer(AuthDbContext dbContext, RoleSeeder roleSeeder)
{
    private const string OutdatedSchemaMessage = "Database schema is not up to date; run the migrate service";

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;", cancellationToken);
    }

    public async Task EnsureCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken)
                || (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            {
                throw new InvalidOperationException(OutdatedSchemaMessage);
            }
        }
        catch (InvalidOperationException exception) when (exception.Message == OutdatedSchemaMessage)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(OutdatedSchemaMessage, exception);
        }
    }

    public Task SeedRolesAsync(CancellationToken cancellationToken = default) => roleSeeder.SeedAsync(cancellationToken);

    public async Task InitializeAsync(bool migrate, CancellationToken cancellationToken = default)
    {
        if (migrate)
        {
            await MigrateAsync(cancellationToken);
        }
        else
        {
            await EnsureCurrentAsync(cancellationToken);
        }

        await SeedRolesAsync(cancellationToken);
    }
}
