using AuthService.Domain.Roles;
using AuthService.Infrastructure;
using AuthService.Infrastructure.Identity;
using AuthService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static AuthService.IntegrationTests.DeploymentTestSupport;

namespace AuthService.IntegrationTests;

public sealed class ProductionStartupTests
{
    private const string OutdatedSchema = "Database schema is not up to date; run the migrate service";

    private static async Task<(IReadOnlyList<string> Pending, IReadOnlyList<string?> Roles)> InspectAsync(AuthServiceFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var pending = (await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.GetPendingMigrationsAsync()).ToList();
        var roles = await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>().Roles.Select(r => r.Name).ToListAsync();
        return (pending, roles);
    }

    [Fact]
    public async Task Minimal_operator_configuration_starts_in_production_and_auto_migrates()
    {
        // Issuer, audience, one CORS origin, bootstrap credentials, MigrateOnStartup=true, no signing settings.
        using var factory = Production();

        var (pending, roles) = await InspectAsync(factory);

        Assert.Empty(pending);
        Assert.Equal([AppRoles.Administrator, AppRoles.Manager, AppRoles.Operator], roles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Absent_migration_setting_does_not_auto_migrate_and_an_outdated_schema_still_fails()
    {
        using var fresh = Production(("Database:MigrateOnStartup", null));
        Assert.Contains(OutdatedSchema, AllMessages(StartFails(fresh)), StringComparison.Ordinal);

        // Migrated installation made outdated: drop the newest migration's table and history row.
        using var migrated = Production();
        await using (var scope = migrated.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database;
            await database.ExecuteSqlRawAsync("DROP TABLE RefreshTokens;");
            await database.ExecuteSqlRawAsync("DELETE FROM __EFMigrationsHistory WHERE MigrationId = (SELECT MAX(MigrationId) FROM __EFMigrationsHistory);");
        }
        using var absent = migrated.Restart(("Database:MigrateOnStartup", null));
        Assert.Contains(OutdatedSchema, AllMessages(StartFails(absent)), StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_migration_prevents_startup_signing_and_bootstrap()
    {
        using var factory = Production();
        var blocker = Path.Combine(factory.RootPath, "not-a-directory");
        File.WriteAllText(blocker, "a file where a directory is needed");
        using var failing = factory.Restart(("Database:Path", Path.Combine(blocker, "auth.db")));

        StartFails(failing);

        Assert.False(Directory.Exists(failing.AutoKeyDirectory), "Signing must not be initialized after a failed migration.");
        Assert.False(File.Exists(factory.DatabasePath), "No database, hence no bootstrap user, may exist.");
    }

    [Fact]
    public async Task Manual_mode_fails_on_an_unmigrated_database_until_the_shared_migration_routine_runs()
    {
        using var factory = Production(("Database:MigrateOnStartup", "false"));
        Assert.Contains(OutdatedSchema, AllMessages(StartFails(factory)), StringComparison.Ordinal);

        // The routine behind `--migrate`: DatabaseInitializer.InitializeAsync(migrate: true).
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Path"] = factory.DatabasePath,
            ["Database:DataProtectionKeysPath"] = Path.Combine(factory.RootPath, "keys"),
        }).Build();
        var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration);
        await using (var provider = services.BuildServiceProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(migrate: true);
        }

        using var afterMigration = factory.Restart();
        var (pending, roles) = await InspectAsync(afterMigration);
        Assert.Empty(pending);
        Assert.Equal(3, roles.Count);
    }
}
