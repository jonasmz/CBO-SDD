using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Tokens;
using Microsoft.Extensions.Options;

namespace AuthService.Api.Security;

public sealed class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience)) return ValidateOptionsResult.Fail("JWT issuer and audience are required.");
        var pem = !string.IsNullOrWhiteSpace(options.Signing.PrivateKeyPemPath);
        var pfx = !string.IsNullOrWhiteSpace(options.Signing.CertificatePath);
        if (!environment.IsDevelopment() && pem == pfx) return ValidateOptionsResult.Fail("Exactly one JWT signing source is required.");
        if (environment.IsDevelopment() && !pem && !pfx) return ValidateOptionsResult.Success;
        try
        {
            using RSA? rsa = pem ? LoadPem(options.Signing.PrivateKeyPemPath!) : LoadPfx(options);
            return rsa is not null && rsa.KeySize >= 2048 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("JWT signing key must be at least 2048 bits.");
        }
        catch { return ValidateOptionsResult.Fail("JWT signing material is missing, unreadable, or invalid."); }
    }
    private static RSA LoadPem(string path) { var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(path)); return rsa; }
    private static RSA? LoadPfx(JwtOptions options)
    {
        if (string.IsNullOrEmpty(options.Signing.CertificatePassword)) throw new InvalidOperationException();
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(options.Signing.CertificatePath!, options.Signing.CertificatePassword);
        return certificate.GetRSAPrivateKey();
    }
}

public sealed class DatabaseStartupValidator(IHostEnvironment environment) : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options) => !environment.IsDevelopment() && options.MigrateOnStartup
        ? ValidateOptionsResult.Fail("Database:MigrateOnStartup cannot be enabled outside Development.") : ValidateOptionsResult.Success;
}

public static class StartupValidation
{
    public static IServiceCollection AddStartupValidation(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseStartupValidator>();
        return services;
    }
}
