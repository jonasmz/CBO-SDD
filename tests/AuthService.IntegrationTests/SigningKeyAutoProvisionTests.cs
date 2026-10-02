using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using static AuthService.IntegrationTests.DeploymentTestSupport;

namespace AuthService.IntegrationTests;

public sealed class SigningKeyAutoProvisionTests
{
    private static readonly TestUser Admin = new(Guid.Empty, "admin@example.test", "ValidPass1", "Administrator");

    [Fact]
    public void First_provision_creates_a_private_pkcs8_rsa_3072_key_with_restricted_permissions()
    {
        using var factory = Production();
        Start(factory);

        var pem = File.ReadAllText(factory.AutoKeyPath);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pem, StringComparison.Ordinal);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        Assert.Equal(3072, rsa.KeySize);
        Assert.Empty(Directory.GetFiles(factory.AutoKeyDirectory, "*.tmp"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(factory.AutoKeyDirectory));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(factory.AutoKeyPath));
        }
        var body = pem.Split('\n').First(line => line.Length > 40 && !line.StartsWith("-----", StringComparison.Ordinal))[..40];
        AssertNoSecretsLogged(factory.Logs, body);
    }

    [Fact]
    public async Task Signing_identity_is_stable_across_restart_and_earlier_tokens_still_validate()
    {
        using var first = Production();
        var (_, token, _) = await first.LoginAsync(Admin);
        var bytes = await File.ReadAllBytesAsync(first.AutoKeyPath);
        var jwks = await JwksAsync(first);

        using var second = first.Restart();
        var client = second.CreateClient();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(second.AutoKeyPath));
        var restartedJwks = await JwksAsync(second);
        Assert.Equal(jwks, restartedJwks);
        Assert.Equal(KidFromJwks(jwks), KidFromJwks(restartedJwks));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public void Missing_key_with_existing_users_fails_without_creating_a_replacement()
    {
        using var first = Production();
        Start(first);
        File.Delete(first.AutoKeyPath);

        using var second = first.Restart();
        var failure = AllMessages(StartFails(second));

        Assert.Contains("Persisted signing key is missing but users exist", failure, StringComparison.Ordinal);
        Assert.Contains("Jwt__Signing__AllowKeyRegeneration", failure, StringComparison.Ordinal);
        Assert.False(File.Exists(second.AutoKeyPath));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("empty")]
    [InlineData("invalid-pem")]
    [InlineData("rsa-1024")]
    [InlineData("ec-key")]
    public void Invalid_persisted_key_fails_startup_and_is_never_replaced_even_in_recovery_mode(string kind)
    {
        using var first = Production();
        Start(first);
        switch (kind)
        {
            case "corrupt": File.WriteAllText(first.AutoKeyPath, "this is not a key"); break;
            case "empty": File.WriteAllText(first.AutoKeyPath, ""); break;
            case "invalid-pem": File.WriteAllText(first.AutoKeyPath, "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n"); break;
            case "rsa-1024": using (var small = RSA.Create(1024)) File.WriteAllText(first.AutoKeyPath, small.ExportPkcs8PrivateKeyPem()); break;
            default: using (var ec = ECDsa.Create()) File.WriteAllText(first.AutoKeyPath, ec.ExportPkcs8PrivateKeyPem()); break;
        }
        var before = File.ReadAllBytes(first.AutoKeyPath);

        foreach (var recovery in new[] { "false", "true" })
        {
            using var restarted = first.Restart(("Jwt:Signing:AllowKeyRegeneration", recovery));
            var failure = AllMessages(StartFails(restarted));
            Assert.DoesNotContain("BEGIN", failure, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(first.AutoKeyPath));
        }
    }

    [UnixNonRootFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void Unreadable_persisted_key_fails_startup_and_is_left_alone()
    {
        using var first = Production();
        Start(first);
        var before = File.ReadAllBytes(first.AutoKeyPath);
        File.SetUnixFileMode(first.AutoKeyPath, UnixFileMode.None);
        try
        {
            foreach (var recovery in new[] { "false", "true" })
            {
                using var restarted = first.Restart(("Jwt:Signing:AllowKeyRegeneration", recovery));
                StartFails(restarted);
            }
        }
        finally
        {
            File.SetUnixFileMode(first.AutoKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        Assert.Equal(before, File.ReadAllBytes(first.AutoKeyPath));
    }

    [Fact]
    public void A_valid_key_is_never_overwritten_by_restart_or_by_recovery_mode()
    {
        using var first = Production();
        Start(first);
        var before = File.ReadAllBytes(first.AutoKeyPath);

        using (var restarted = first.Restart()) { Start(restarted); }
        Assert.Equal(before, File.ReadAllBytes(first.AutoKeyPath));

        using (var recovery = first.Restart(("Jwt:Signing:AllowKeyRegeneration", "true"))) { Start(recovery); }
        Assert.Equal(before, File.ReadAllBytes(first.AutoKeyPath));
    }

    [Fact]
    public async Task Recovery_mode_deliberately_replaces_an_absent_key_with_a_new_identity_and_warns()
    {
        using var first = Production();
        var originalKid = KidFromJwks(await JwksAsync(first));
        File.Delete(first.AutoKeyPath);

        using var recovered = first.Restart(("Jwt:Signing:AllowKeyRegeneration", "true"));
        var newKid = KidFromJwks(await JwksAsync(recovered));

        Assert.NotEqual(originalKid, newKid);
        Assert.True(File.Exists(recovered.AutoKeyPath));
        Assert.Contains(recovered.Logs.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("recovery mode", StringComparison.Ordinal));
        Assert.Contains(recovered.Logs.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("replaced", StringComparison.Ordinal) && e.Text.Contains(newKid, StringComparison.Ordinal));
        AssertNoSecretsLogged(recovered.Logs);

        // Flag still on, key now present: same key is reused and the recovery warning is emitted again.
        var bytes = File.ReadAllBytes(recovered.AutoKeyPath);
        using var again = first.Restart(("Jwt:Signing:AllowKeyRegeneration", "true"));
        Assert.Equal(newKid, KidFromJwks(await JwksAsync(again)));
        Assert.Equal(bytes, File.ReadAllBytes(recovered.AutoKeyPath));
        Assert.Contains(again.Logs.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("recovery mode", StringComparison.Ordinal));
        Assert.DoesNotContain(again.Logs.Entries, e => e.Text.Contains("replaced", StringComparison.Ordinal));
        AssertNoSecretsLogged(again.Logs);

        // Corrupt key + flag: still fails and the file is untouched.
        File.WriteAllText(recovered.AutoKeyPath, "corrupt");
        using var corrupt = first.Restart(("Jwt:Signing:AllowKeyRegeneration", "true"));
        StartFails(corrupt);
        Assert.Equal("corrupt", File.ReadAllText(recovered.AutoKeyPath));
    }
}
