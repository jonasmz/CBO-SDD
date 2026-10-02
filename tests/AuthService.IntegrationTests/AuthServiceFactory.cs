using System.Net.Http.Headers;
using System.Security.Cryptography;
using AuthService.Domain.Roles;
using AuthService.Infrastructure.Identity;
using AuthService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

namespace AuthService.IntegrationTests;

public sealed class AuthServiceFactory : WebApplicationFactory<Program>
{
    private readonly string _rootPath;
    private readonly bool _ownsRootPath;

    public AuthServiceFactory() : this(Path.Combine(Path.GetTempPath(), $"auth-service-tests-{Guid.NewGuid():N}"), true) { }

    private AuthServiceFactory(string rootPath, bool ownsRootPath)
    {
        _rootPath = rootPath;
        _ownsRootPath = ownsRootPath;
        Directory.CreateDirectory(_rootPath);
        DatabasePath = Path.Combine(_rootPath, "auth.db");
        KeyPath = Path.Combine(_rootPath, "signing-key.pem");
        using var rsa = RSA.Create(2048);
        File.WriteAllText(KeyPath, rsa.ExportRSAPrivateKeyPem());
        Time = new FakeTimeProvider(DateTimeOffset.UtcNow);
    }

    public string DatabasePath { get; }
    public string KeyPath { get; }
    public FakeTimeProvider Time { get; }

    public AuthServiceFactory Restart() => new(_rootPath, false);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            foreach (var source in configuration.Sources.OfType<EnvironmentVariablesConfigurationSource>().ToArray())
            {
                configuration.Sources.Remove(source);
            }
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
            ["Database:Path"] = DatabasePath,
            ["Database:DataProtectionKeysPath"] = Path.Combine(_rootPath, "keys"),
            ["Database:MigrateOnStartup"] = "true",
            ["Jwt:Issuer"] = "https://issuer.integration.test",
            ["Jwt:Audience"] = "integration-consumer",
            ["Jwt:AccessTokenMinutes"] = "15",
            ["Jwt:Signing:PrivateKeyPemPath"] = KeyPath,
            ["Bootstrap:AdminEmail"] = "",
            ["Bootstrap:AdminPassword"] = "",
            ["Session:RefreshTokenDays"] = "1",
            ["Session:SessionMaxDays"] = "2",
                ["RateLimit:PermitLimit"] = "1000"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    public async Task<TestUser> CreateUserAsync(string role, bool enabled = true, string? email = null, string password = "ValidPass1")
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var address = email ?? $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = address, UserName = address, IsEnabled = enabled, CreatedAt = Time.GetUtcNow() };
        // Keep the identifier consistent when a generated address was used.
        user.UserName = user.Email;
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Code)));
        var roles = await users.AddToRoleAsync(user, role);
        Assert.True(roles.Succeeded, string.Join(", ", roles.Errors.Select(error => error.Code)));
        return new TestUser(user.Id, user.Email!, password, role);
    }

    public async Task<(HttpClient Client, string AccessToken, string RefreshToken)> LoginAsync(TestUser user)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, user.Password });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(payload);
        return (client, payload.AccessToken, payload.RefreshToken);
    }

    public async Task<ApplicationUser?> FindUserAsync(Guid id)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(id.ToString());
    }

    public async Task<int> RefreshTokenCountAsync(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().RefreshTokens.CountAsync(token => token.UserId == userId);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsRootPath && Directory.Exists(_rootPath)) Directory.Delete(_rootPath, true);
    }
}

public sealed record TestUser(Guid Id, string Email, string Password, string Role);
public sealed record TokenResponse(string AccessToken, long ExpiresInSeconds, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
