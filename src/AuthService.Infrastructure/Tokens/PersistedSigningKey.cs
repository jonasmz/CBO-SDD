using System.Security.Cryptography;
using AuthService.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthService.Infrastructure.Tokens;

/// <summary>
/// Automatically managed signing key, used only when no explicit PEM/PFX source is configured.
/// Startup calls <see cref="EnsureAsync"/> once (after migrations, before bootstrap) with the installation state;
/// <see cref="SigningKeyProvider"/> then reads the already-ensured key. The key is never generated lazily
/// and an existing file is never overwritten, repaired or deleted.
/// </summary>
public sealed partial class PersistedSigningKey(
    IOptions<JwtOptions> jwtOptions,
    IOptions<DatabaseOptions> databaseOptions,
    ILogger<PersistedSigningKey> logger)
{
    private const int GeneratedKeySize = 3072;
    private const int MinimumKeySize = 2048;
    private RSA? key;

    /// <summary>Effective key file: configured <c>AutoKeyPath</c>, else <c>&lt;Database.Path directory&gt;/signing/signing.pem</c>.</summary>
    public string KeyPath
    {
        get
        {
            var configured = jwtOptions.Value.Signing.AutoKeyPath;
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(databaseOptions.Value.Path))!;
            return Path.Combine(dataDirectory, "signing", "signing.pem");
        }
    }

    public RSA GetKey() => key ?? throw new InvalidOperationException("Persisted signing key has not been initialized at startup.");

    public Task EnsureAsync(bool usersExist, bool allowRegeneration, CancellationToken cancellationToken = default)
    {
        if (allowRegeneration) Log.RecoveryModeEnabled(logger);
        var path = KeyPath;

        if (File.Exists(path))
        {
            // Present: reuse if valid, otherwise fail. Never regenerate, even in recovery mode.
            key = Load(path);
            return Task.CompletedTask;
        }

        if (usersExist && !allowRegeneration)
        {
            throw new InvalidOperationException(
                "Persisted signing key is missing but users exist; restore the key from backup or set "
                + "Jwt__Signing__AllowKeyRegeneration=true to deliberately replace it.");
        }

        key = GenerateAndPersist(path);
        var keyId = SigningKeyProvider.ComputeKeyId(key);
        if (usersExist) Log.KeyReplaced(logger, keyId);
        else Log.KeyGenerated(logger, keyId);
        return Task.CompletedTask;
    }

    private static RSA Load(string path)
    {
        RSA? rsa = null;
        try
        {
            rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(path));
            if (rsa.KeySize < MinimumKeySize) throw new CryptographicException();
            return rsa;
        }
        catch (Exception)
        {
            rsa?.Dispose();
            // Deliberately secret-free: no key text and no inner exception.
            throw new InvalidOperationException(
                $"Persisted signing key at '{path}' is unreadable, malformed, not an RSA key, or smaller than {MinimumKeySize} bits; it is left unchanged and must be restored or fixed by the operator.");
        }
    }

    private static RSA GenerateAndPersist(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var generated = RSA.Create(GeneratedKeySize);
        var temporary = Path.Combine(directory, $".signing-{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(generated.ExportPkcs8PrivateKeyPem());
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another process provisioned the key first: keep theirs, never overwrite.
                generated.Dispose();
                return Load(path);
            }

            return generated;
        }
        catch
        {
            generated.Dispose();
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Generated a new persisted JWT signing key with kid {KeyId}")]
        public static partial void KeyGenerated(ILogger logger, string keyId);

        [LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "Jwt:Signing:AllowKeyRegeneration is enabled (recovery mode). Remove it once recovery is complete.")]
        public static partial void RecoveryModeEnabled(ILogger logger);

        [LoggerMessage(EventId = 14, Level = LogLevel.Warning, Message = "JWT signing key was replaced (new kid {KeyId}); access tokens issued with the previous key will no longer validate.")]
        public static partial void KeyReplaced(ILogger logger, string keyId);
    }
}
