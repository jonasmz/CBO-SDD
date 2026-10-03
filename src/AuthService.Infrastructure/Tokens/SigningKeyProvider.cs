using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Infrastructure.Tokens;

public sealed partial class SigningKeyProvider
{
    private readonly RSA privateKey;
    public SigningCredentials Credentials { get; }
    public string KeyId { get; }
    public IReadOnlyList<JsonWebKey> PublicKeys { get; }

    public SigningKeyProvider(IOptions<JwtOptions> options, IHostEnvironment environment, ILogger<SigningKeyProvider> logger, PersistedSigningKey persistedKey)
    {
        var configuration = options.Value;
        X509Certificate2? certificate = null;
        if (!string.IsNullOrWhiteSpace(configuration.Signing.CertificatePath))
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(configuration.Signing.CertificatePath, configuration.Signing.CertificatePassword);
            privateKey = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("JWT signing certificate has no RSA private key.");
        }
        else
        {
            var path = configuration.Signing.PrivateKeyPemPath;
            if (string.IsNullOrWhiteSpace(path) && !environment.IsDevelopment())
            {
                // No explicit source: use the persisted key that startup already ensured (never generated lazily here).
                privateKey = persistedKey.GetKey();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = "/data/dev-keys/signing.pem";
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (!File.Exists(path)) { using var generated = RSA.Create(2048); File.WriteAllText(path, generated.ExportRSAPrivateKeyPem()); }
                }
                privateKey = RSA.Create();
                privateKey.ImportFromPem(File.ReadAllText(path));
            }
        }
        if (privateKey.KeySize < 2048) throw new InvalidOperationException("JWT signing key must be at least 2048 bits.");
        KeyId = ComputeKeyId(privateKey);
        Credentials = new SigningCredentials(new RsaSecurityKey(privateKey) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256);
        var keys = new List<JsonWebKey> { ToJwk(privateKey, KeyId) };
        foreach (var path in configuration.Signing.PreviousPublicKeyPaths) { using var previous = LoadPublicKey(path); keys.Add(ToJwk(previous, ComputeKeyId(previous))); }
        PublicKeys = keys;
        Log.KeyLoaded(logger, KeyId);
        if (certificate is not null) Log.CertificateExpiry(logger, certificate.NotAfter);
        certificate?.Dispose();
    }

    internal static string ComputeKeyId(RSA rsa) => Base64UrlEncoder.Encode(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
    private static JsonWebKey ToJwk(RSA rsa, string keyId)
    {
        var p = rsa.ExportParameters(false);
        return new JsonWebKey { Kty = "RSA", N = Base64UrlEncoder.Encode(p.Modulus), E = Base64UrlEncoder.Encode(p.Exponent), Kid = keyId, Alg = SecurityAlgorithms.RsaSha256, Use = "sig" };
    }
    private static RSA LoadPublicKey(string path)
    {
        var rsa = RSA.Create();
        try { rsa.ImportFromPem(File.ReadAllText(path)); return rsa; }
        catch
        {
            rsa.Dispose();
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
            return certificate.GetRSAPublicKey() ?? throw new InvalidOperationException("Previous key is not RSA.");
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "JWT signing key loaded with kid {KeyId}")]
        public static partial void KeyLoaded(ILogger logger, string keyId);
        [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "JWT signing certificate expires at {ExpiresAt}")]
        public static partial void CertificateExpiry(ILogger logger, DateTime expiresAt);
    }
}
