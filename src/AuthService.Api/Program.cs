using AuthService.Api.Hosting;
using AuthService.Application.Abstractions;
using AuthService.Application.Features.Bootstrap.BootstrapAdministrator;
using AuthService.Infrastructure;
using AuthService.Infrastructure.Identity;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Tokens;
using AuthService.Api.Security;
using AuthService.Api.Features.Authentication;
using AuthService.Api.Features.Metadata;
using AuthService.Api.Features.Identity;
using AuthService.Api.Features.Users;
using AuthService.Api.OpenApi;
using AuthService.Application.Features.Authentication.Login;
using AuthService.Application.Features.Authentication.Logout;
using AuthService.Application.Features.Authentication.RefreshToken;
using AuthService.Application.Features.Authentication.ResetPassword;
using AuthService.Application.Features.Identity.GetCurrentUser;
using AuthService.Application.Features.Users;
using AuthService.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

if (Directory.Exists("/run/secrets/config"))
{
    builder.Configuration.AddKeyPerFile("/run/secrets/config", optional: true);
}

// Bootstrap values are validated only when the user store is empty (see BootstrapRunner), never at option binding.
builder.Services.AddOptions<BootstrapOptions>()
    .Bind(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiOpenApi(builder.Environment);
builder.Services.AddApiProblemDetails();
builder.Services.AddConfiguredCors(builder.Configuration, builder.Environment);
builder.Services.AddAuthRateLimiting(builder.Configuration);
builder.Services.AddStartupValidation();
builder.Services.AddOptions<JwtOptions>().Bind(builder.Configuration.GetSection(JwtOptions.SectionName)).ValidateDataAnnotations().Validate(x => !string.IsNullOrWhiteSpace(x.Issuer) && !string.IsNullOrWhiteSpace(x.Audience), "JWT issuer and audience are required.").ValidateOnStart();
builder.Services.AddJwtBearerSetup();
builder.Services.AddAuthorizationPolicies();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<RefreshTokenHandler>();
builder.Services.AddScoped<LogoutHandler>();
builder.Services.AddScoped<ResetPasswordHandler>();
builder.Services.AddScoped<GetCurrentUserHandler>();
builder.Services.AddScoped<ListUsersHandler>();
builder.Services.AddScoped<GetUserHandler>();
builder.Services.AddScoped<CreateUserHandler>();
builder.Services.AddScoped<SetUserEnabledHandler>();
builder.Services.AddScoped<ChangeUserRoleHandler>();
builder.Services.AddScoped<IssuePasswordResetHandler>();
builder.Services.AddScoped<BootstrapAdministratorHandler>();
builder.Services.AddSingleton<BootstrapRunner>();

var useForwardedHeaders = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    });
}

var app = builder.Build();

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    using var client = new HttpClient();
    try { Environment.ExitCode = (await client.GetAsync("http://localhost:8080/health")).IsSuccessStatusCode ? 0 : 1; } catch { Environment.ExitCode = 1; }
    return;
}

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await ApplyMigrationsAndSeedAsync(app.Services);
    return;
}

ValidateStartupOptions(app.Services);
await InitializeAsync(app.Services);
app.UseApiProblemDetails();
if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}
app.UseCors(CorsSetup.Policy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapJwks();
app.MapMe();
app.MapUsers();
app.MapHealth();
app.MapApiOpenApi();
app.MapApiScalar();
app.Run();

static void ValidateStartupOptions(IServiceProvider services)
{
    _ = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    _ = services.GetRequiredService<IOptions<IdentityAccountOptions>>().Value;
    _ = services.GetRequiredService<IOptions<AuthService.Infrastructure.Sessions.SessionOptions>>().Value;
    _ = services.GetRequiredService<IOptions<CorsOptions>>().Value;
    _ = services.GetRequiredService<IOptions<RateLimitOptions>>().Value;
    _ = services.GetRequiredService<IOptions<JwtOptions>>().Value;
    _ = services.GetRequiredService<IOptions<BootstrapOptions>>().Value;
}

static async Task ApplyMigrationsAndSeedAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(migrate: true);
}

// Startup initialization. Any failure aborts the process before app.Run(), so nothing is served half-initialized.
// Order: migrations/schema check -> fixed roles -> signing key -> bootstrap Administrator.
// Signing precedes bootstrap because it must observe the installation state BEFORE the first user exists:
// "users exist but the generated key is missing" is key loss, whereas "no users yet" is first provisioning.
static async Task InitializeAsync(IServiceProvider services)
{
    var jwt = services.GetRequiredService<IOptions<JwtOptions>>().Value;
    var environment = services.GetRequiredService<IHostEnvironment>();

    bool usersExist;
    await using (var scope = services.CreateAsyncScope())
    {
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(options.MigrateOnStartup);
        usersExist = await scope.ServiceProvider.GetRequiredService<IUserDirectory>().CountAsync() > 0;
    }

    var explicitSigningSource = !string.IsNullOrWhiteSpace(jwt.Signing.PrivateKeyPemPath)
        || !string.IsNullOrWhiteSpace(jwt.Signing.CertificatePath);
    if (!explicitSigningSource && !environment.IsDevelopment())
    {
        await services.GetRequiredService<PersistedSigningKey>().EnsureAsync(usersExist, jwt.Signing.AllowKeyRegeneration);
    }

    // Resolve eagerly so signing problems surface here rather than on the first token request.
    _ = services.GetRequiredService<SigningKeyProvider>();

    await services.GetRequiredService<BootstrapRunner>().RunAsync();
}

public partial class Program;
