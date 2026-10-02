using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.IntegrationTests;

/// <summary>Skips a Unix file-permission test where it cannot be meaningful (Windows, or running as root).</summary>
public sealed class UnixNonRootFactAttribute : FactAttribute
{
    public UnixNonRootFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "File-permission semantics are Unix-specific.";
        else if (Environment.IsPrivilegedProcess) Skip = "Running as root: unreadable-file semantics cannot be exercised (root bypasses file permissions).";
    }
}

internal static class DeploymentTestSupport
{
    public const string PrivateKeyMarker = "PRIVATE KEY";

    public static AuthServiceFactory Production(params (string Key, string? Value)[] settings) =>
        new(AuthServiceFactoryOptions.Production(settings));

    /// <summary>Starts the host (it starts lazily); startup/initialization failures surface here.</summary>
    public static void Start(AuthServiceFactory factory) => _ = factory.Services;

    public static Exception StartFails(AuthServiceFactory factory) => Assert.ThrowsAny<Exception>(() => Start(factory));

    public static string AllMessages(Exception exception)
    {
        var messages = new List<string>();
        void Walk(Exception? current)
        {
            while (current is not null)
            {
                messages.Add(current.Message);
                if (current is AggregateException aggregate) foreach (var inner in aggregate.InnerExceptions) Walk(inner);
                current = current.InnerException;
            }
        }
        Walk(exception);
        return string.Join(" | ", messages);
    }

    public static async Task<string> JwksAsync(AuthServiceFactory factory) =>
        await factory.CreateClient().GetStringAsync("/.well-known/jwks.json");

    public static string KidFromJwks(string jwks) =>
        JsonDocument.Parse(jwks).RootElement.GetProperty("keys")[0].GetProperty("kid").GetString()!;

    /// <summary>Same derivation as the service: base64url(SHA-256(SubjectPublicKeyInfo)).</summary>
    public static string Kid(RSA rsa) => Base64UrlEncoder.Encode(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));

    public static string KidOfPem(string path)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));
        return Kid(rsa);
    }

    public static string WritePem(string directory, string name, int bits = 2048)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using var rsa = RSA.Create(bits);
        File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        return path;
    }

    public static (string Path, RSA PublicKey) WritePfx(string directory, string name, string password)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=deployment-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        var publicOnly = RSA.Create();
        publicOnly.ImportSubjectPublicKeyInfo(rsa.ExportSubjectPublicKeyInfo(), out _);
        return (path, publicOnly);
    }

    public static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "auth-service-deploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Asserts no log line contains private-key text or the given secrets.</summary>
    public static void AssertNoSecretsLogged(LogCapture logs, params string?[] secrets)
    {
        foreach (var (_, text) in logs.Entries)
        {
            Assert.DoesNotContain(PrivateKeyMarker, text, StringComparison.Ordinal);
            foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s))) Assert.DoesNotContain(secret!, text, StringComparison.Ordinal);
        }
    }
}
