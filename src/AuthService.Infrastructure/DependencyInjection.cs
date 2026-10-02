using AuthService.Domain.Security;
using AuthService.Application.Abstractions;
using AuthService.Infrastructure.Tokens;
using AuthService.Infrastructure.Identity;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Sessions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AuthService.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<IdentityAccountOptions>()
            .Bind(configuration.GetSection(IdentityAccountOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SessionOptions>()
            .Bind(configuration.GetSection(SessionOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.SessionMaxDays >= options.RefreshTokenDays,
                "Session maximum lifetime must be greater than or equal to refresh-token lifetime.")
            .ValidateOnStart();

        services.AddDbContext<AuthDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseSqlite($"Data Source={database.Path}");
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                var identity = configuration.GetSection(IdentityAccountOptions.SectionName).Get<IdentityAccountOptions>()
                    ?? new IdentityAccountOptions();

                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = PasswordPolicy.MinLength;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = identity.LockoutMaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(identity.LockoutMinutes);
                options.Lockout.AllowedForNewUsers = true;
                options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            var identity = configuration.GetSection(IdentityAccountOptions.SectionName).Get<IdentityAccountOptions>()
                ?? new IdentityAccountOptions();
            options.TokenLifespan = TimeSpan.FromHours(identity.ResetCodeHours);
        });

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? new DatabaseOptions();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(databaseOptions.DataProtectionKeysPath));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IUserCreator, UserCreator>();
        services.AddScoped<IUserStateStore, UserStateStore>();
        services.AddScoped<IUserRoleStore, UserRoleStore>();
        services.AddScoped<IPasswordResetCodes, PasswordResetCodes>();
        services.AddScoped<ICredentialVerifier, CredentialVerifier>();
        services.AddScoped<RefreshTokenStore>();
        services.AddScoped<IRefreshTokenStore>(provider => provider.GetRequiredService<RefreshTokenStore>());
        services.AddScoped<IUserSessionRevoker>(provider => provider.GetRequiredService<RefreshTokenStore>());
        services.AddSingleton<SigningKeyProvider>();
        services.AddSingleton<JwksBuilder>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<RoleSeeder>();
        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
