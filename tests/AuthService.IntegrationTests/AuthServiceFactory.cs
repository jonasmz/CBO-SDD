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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

namespace AuthService.IntegrationTests;

/// <summary>
/// Optional shape of a test host. The default (no options) is the original Development host with an explicit PEM and
/// blank bootstrap, so existing tests are unaffected. <see cref="Production"/> builds the minimal operator
/// configuration with automatic signing; <see cref="Settings"/> override or (with null) remove any configuration key.
/// </summary>
public sealed record AuthServiceFactoryOptions(string Environment = "Development", bool ExplicitPem = true, IReadOnlyDictionary<string, string?>? Settings = null)
{
    public static AuthServiceFactoryOptions Production(params (string Key, string? Value)[] settings) =>
        new("Production", false, settings.ToDictionary(x => x.Key, x => x.Value));

    public AuthServiceFactoryOptions With(params (string Key, string? Value)[] settings)
    {
        var merged = new Dictionary<string, string?>(Settings ?? new Dictionary<string, string?>());
        foreach (var (key, value) in settings) merged[key] = value;
        return this with { Settings = merged };
    }
}

public sealed class LogCapture : ILoggerProvider, ILogger
{
    private readonly List<(LogLevel Level, string Text)> _entries = [];
    public IReadOnlyList<(LogLevel Level, string Text)> Entries { get { lock (_entries) return [.. _entries]; } }
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries) _entries.Add((logLevel, formatter(state, exception) + exception));
    }
    public void Dispose() { }
}

public sealed class AuthServiceFactory : WebApplicationFactory<Program>
{
    private readonly string _rootPath;
    private readonly bool _ownsRootPath;
    private readonly AuthServiceFactoryOptions _options;

    public AuthServiceFactory() : this(new AuthServiceFactoryOptions()) { }

    internal AuthServiceFactory(AuthServiceFactoryOptions options)
        : this(Path.Combine(Path.GetTempPath(), $"auth-service-tests-{Guid.NewGuid():N}"), true, options) { }

    private AuthServiceFactory(string rootPath, bool ownsRootPath, AuthServiceFactoryOptions options)
    {
        _rootPath = rootPath;
        _ownsRootPath = ownsRootPath;
        _options = options;
        Directory.CreateDirectory(_rootPath);
        DatabasePath = Path.Combine(_rootPath, "auth.db");
        KeyPath = Path.Combine(_rootPath, "signing-key.pem");
        if (options.ExplicitPem && !File.Exists(KeyPath))
        {
            using var rsa = RSA.Create(2048);
            File.WriteAllText(KeyPath, rsa.ExportRSAPrivateKeyPem());
        }
        Time = new FakeTimeProvider(DateTimeOffset.UtcNow);
    }

    /// <summary>Shared data root (same role as /data in production); survives <see cref="Restart()"/>.</summary>
    public string RootPath => _rootPath;
    /// <summary>Where the automatically managed signing key lives: <c>&lt;root&gt;/signing/signing.pem</c>.</summary>
    public string AutoKeyDirectory => Path.Combine(_rootPath, "signing");
    public string AutoKeyPath => Path.Combine(AutoKeyDirectory, "signing.pem");
    public string DatabasePath { get; }
    public string KeyPath { get; }
    public FakeTimeProvider Time { get; }
    public LogCapture Logs { get; } = new();

    public AuthServiceFactory Restart() => new(_rootPath, false, _options);

    /// <summary>Restart over the same data root, changing configuration keys (null removes a key).</summary>
    public AuthServiceFactory Restart(params (string Key, string? Value)[] settings) => new(_rootPath, false, _options.With(settings));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_options.Environment);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            foreach (var source in configuration.Sources.OfType<EnvironmentVariablesConfigurationSource>().ToArray())
            {
                configuration.Sources.Remove(source);
            }
            var production = !string.Equals(_options.Environment, "Development", StringComparison.Ordinal);
            var values = new Dictionary<string, string?>
            {
                ["Database:Path"] = DatabasePath,
                ["Database:DataProtectionKeysPath"] = Path.Combine(_rootPath, "keys"),
                ["Database:MigrateOnStartup"] = "true",
                ["Jwt:Issuer"] = "https://issuer.integration.test",
                ["Jwt:Audience"] = "integration-consumer",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Bootstrap:AdminEmail"] = production ? "admin@example.test" : "",
                ["Bootstrap:AdminPassword"] = production ? "ValidPass1" : "",
                ["Session:RefreshTokenDays"] = "1",
                ["Session:SessionMaxDays"] = "2",
                ["RateLimit:PermitLimit"] = "1000"
            };
            if (_options.ExplicitPem) values["Jwt:Signing:PrivateKeyPemPath"] = KeyPath;
            if (production) values["Cors:AllowedOrigins:0"] = "https://app.example.test";
            foreach (var (key, value) in _options.Settings ?? new Dictionary<string, string?>())
            {
                if (value is null) values.Remove(key); else values[key] = value;
            }
            configuration.AddInMemoryCollection(values);
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
