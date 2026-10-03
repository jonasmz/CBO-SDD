using AuthService.Infrastructure.Identity;
using AuthService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static AuthService.IntegrationTests.DeploymentTestSupport;

namespace AuthService.IntegrationTests;

public sealed class BootstrapStartupTests
{
    private static async Task<List<string?>> UserEmailsAsync(AuthServiceFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.Select(u => u.Email).ToListAsync();
    }

    [Fact]
    public async Task First_bootstrap_runs_after_migration_and_signing_initialization_and_login_works_immediately()
    {
        using var factory = Production();
        var admin = new TestUser(Guid.Empty, "admin@example.test", "ValidPass1", "Administrator");

        var (client, _, _) = await factory.LoginAsync(admin);

        Assert.True(File.Exists(factory.AutoKeyPath));
        Assert.Contains("\"kid\"", await client.GetStringAsync("/.well-known/jwks.json"), StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsInRoleAsync((await users.FindByEmailAsync(admin.Email))!, "Administrator"));
        var lines = factory.Logs.Entries.Select(e => e.Text).ToList();
        var keyGenerated = lines.FindIndex(t => t.Contains("Generated a new persisted JWT signing key", StringComparison.Ordinal));
        var bootstrapCreated = lines.FindIndex(t => t.Contains("Bootstrap administrator created", StringComparison.Ordinal));
        Assert.True(keyGenerated >= 0 && bootstrapCreated > keyGenerated, "The signing key must be provisioned before the first user is created.");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("admin@example.test", null)]
    [InlineData(null, "OnlyPassword123")]
    [InlineData("not-an-email", "ValidPass1")]
    [InlineData("admin@example.test", "alllowercase")]
    public async Task Invalid_bootstrap_on_an_empty_store_fails_startup_without_leaking_the_password(string? email, string? password)
    {
        using var failing = Production(("Bootstrap:AdminEmail", email), ("Bootstrap:AdminPassword", password));

        var failure = AllMessages(StartFails(failing));

        Assert.Contains("Bootstrap Administrator configuration is missing or invalid", failure, StringComparison.Ordinal);
        AssertNoSecretsLogged(failing.Logs, password);
        if (!string.IsNullOrEmpty(password)) Assert.DoesNotContain(password, failure, StringComparison.Ordinal);

        // The failed attempt left no user behind: a valid configuration then creates exactly one Administrator.
        using var valid = failing.Restart(("Bootstrap:AdminEmail", "admin@example.test"), ("Bootstrap:AdminPassword", "ValidPass1"));
        Assert.Equal(["admin@example.test"], await UserEmailsAsync(valid));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("partial-email-only")]
    [InlineData("partial-password-only")]
    [InlineData("invalid")]
    [InlineData("changed")]
    public async Task Existing_users_make_bootstrap_configuration_irrelevant(string variant)
    {
        using var first = Production();
        Assert.Equal(["admin@example.test"], await UserEmailsAsync(first));

        var settings = variant switch
        {
            "absent" => new (string, string?)[] { ("Bootstrap:AdminEmail", null), ("Bootstrap:AdminPassword", null) },
            "partial-email-only" => [("Bootstrap:AdminEmail", "someone@example.test"), ("Bootstrap:AdminPassword", null)],
            "partial-password-only" => [("Bootstrap:AdminEmail", null), ("Bootstrap:AdminPassword", "ValidPass1")],
            "invalid" => [("Bootstrap:AdminEmail", "not-an-email"), ("Bootstrap:AdminPassword", "weak")],
            _ => [("Bootstrap:AdminEmail", "different@example.test"), ("Bootstrap:AdminPassword", "AnotherPass1")],
        };
        using var restarted = first.Restart(settings);

        Assert.Equal(["admin@example.test"], await UserEmailsAsync(restarted));
        Assert.DoesNotContain(restarted.Logs.Entries, e => e.Text.Contains("Bootstrap administrator created", StringComparison.Ordinal));
        Assert.Contains(restarted.Logs.Entries, e => e.Text.Contains("users already exist", StringComparison.Ordinal));
    }
}
