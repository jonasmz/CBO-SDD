using AuthService.Api.Hosting;
using AuthService.Application.Features.Bootstrap.BootstrapAdministrator;
using AuthService.Infrastructure;
using AuthService.Infrastructure.Identity;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

if (Directory.Exists("/run/secrets/config"))
{
    builder.Configuration.AddKeyPerFile("/run/secrets/config", optional: true);
}

builder.Services.AddOptions<BootstrapOptions>()
    .Bind(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<BootstrapAdministratorHandler>();
builder.Services.AddHostedService<BootstrapHostedService>();

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await ApplyMigrationsAndSeedAsync(app.Services);
    return;
}

ValidateStartupOptions(app.Services);
await PrepareDatabaseAsync(app.Services);
app.Run();

static void ValidateStartupOptions(IServiceProvider services)
{
    _ = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    _ = services.GetRequiredService<IOptions<IdentityAccountOptions>>().Value;
}

static async Task ApplyMigrationsAndSeedAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await database.Database.MigrateAsync();
    await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");
    await scope.ServiceProvider.GetRequiredService<RoleSeeder>().SeedAsync();
}

static async Task PrepareDatabaseAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

    if (options.MigrateOnStartup)
    {
        await database.Database.MigrateAsync();
        await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");
    }
    else
    {
        try
        {
            if (!await database.Database.CanConnectAsync() || (await database.Database.GetPendingMigrationsAsync()).Any())
            {
                throw new InvalidOperationException("Database schema is not up to date; run the migrate service");
            }
        }
        catch (InvalidOperationException exception) when (exception.Message == "Database schema is not up to date; run the migrate service")
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Database schema is not up to date; run the migrate service", exception);
        }
    }

    await scope.ServiceProvider.GetRequiredService<RoleSeeder>().SeedAsync();
}

public partial class Program;
