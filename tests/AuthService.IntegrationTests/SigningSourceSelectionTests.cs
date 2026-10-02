using static AuthService.IntegrationTests.DeploymentTestSupport;

namespace AuthService.IntegrationTests;

public sealed class SigningSourceSelectionTests : IDisposable
{
    private const string PfxPassword = "pfx-test-secret";
    private readonly string _secrets = NewTempDirectory();

    public void Dispose() => DeleteQuietly(_secrets);

    [Fact]
    public async Task Explicit_pem_is_used_and_the_auto_signing_state_is_never_touched()
    {
        var pem = WritePem(_secrets, "explicit.pem");
        using var factory = Production(("Jwt:Signing:PrivateKeyPemPath", pem));

        Assert.Equal(KidOfPem(pem), KidFromJwks(await JwksAsync(factory)));
        Assert.False(Directory.Exists(factory.AutoKeyDirectory));

        // An auto key that already exists is neither used nor modified.
        using var withExisting = Production(("Jwt:Signing:PrivateKeyPemPath", pem));
        Directory.CreateDirectory(withExisting.AutoKeyDirectory);
        var autoKey = WritePem(withExisting.AutoKeyDirectory, "signing.pem");
        var before = File.ReadAllBytes(autoKey);
        Assert.Equal(KidOfPem(pem), KidFromJwks(await JwksAsync(withExisting)));
        Assert.NotEqual(KidOfPem(autoKey), KidFromJwks(await JwksAsync(withExisting)));
        Assert.Equal(before, File.ReadAllBytes(autoKey));
    }

    [Fact]
    public async Task Explicit_pfx_is_used_and_the_auto_signing_state_is_never_touched()
    {
        var (pfx, publicKey) = WritePfx(_secrets, "explicit.pfx", PfxPassword);
        using var factory = Production(("Jwt:Signing:CertificatePath", pfx), ("Jwt:Signing:CertificatePassword", PfxPassword));

        Assert.Equal(Kid(publicKey), KidFromJwks(await JwksAsync(factory)));
        Assert.False(Directory.Exists(factory.AutoKeyDirectory));
        publicKey.Dispose();
    }

    [Theory]
    [InlineData("pem-and-pfx")]
    [InlineData("pfx-without-password")]
    [InlineData("password-without-pfx")]
    [InlineData("missing-pem")]
    [InlineData("missing-pfx")]
    [InlineData("wrong-pfx-password")]
    [InlineData("pem-below-2048-bits")]
    public void Invalid_explicit_signing_configuration_fails_startup_with_secret_free_messages(string scenario)
    {
        var pem = WritePem(_secrets, "valid.pem");
        var (pfx, publicKey) = WritePfx(_secrets, "valid.pfx", PfxPassword);
        publicKey.Dispose();
        var settings = scenario switch
        {
            "pem-and-pfx" => new[] { ("Jwt:Signing:PrivateKeyPemPath", pem), ("Jwt:Signing:CertificatePath", pfx), ("Jwt:Signing:CertificatePassword", PfxPassword) },
            "pfx-without-password" => new[] { ("Jwt:Signing:CertificatePath", pfx) },
            "password-without-pfx" => new[] { ("Jwt:Signing:CertificatePassword", PfxPassword) },
            "missing-pem" => new[] { ("Jwt:Signing:PrivateKeyPemPath", Path.Combine(_secrets, "missing.pem")) },
            "missing-pfx" => new[] { ("Jwt:Signing:CertificatePath", Path.Combine(_secrets, "missing.pfx")), ("Jwt:Signing:CertificatePassword", PfxPassword) },
            "wrong-pfx-password" => new[] { ("Jwt:Signing:CertificatePath", pfx), ("Jwt:Signing:CertificatePassword", "not-the-password") },
            _ => new[] { ("Jwt:Signing:PrivateKeyPemPath", WritePem(_secrets, "small.pem", 1024)) },
        };
        using var factory = Production(settings.Select(s => (s.Item1, (string?)s.Item2)).ToArray());

        var failure = AllMessages(StartFails(factory));

        Assert.DoesNotContain(PfxPassword, failure, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-password", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateKeyMarker, failure, StringComparison.Ordinal);
        AssertNoSecretsLogged(factory.Logs, PfxPassword, "not-the-password");
        Assert.False(Directory.Exists(factory.AutoKeyDirectory), "A misconfigured explicit source must not fall back to automatic signing.");
        if (scenario == "pem-and-pfx") Assert.Contains("Only one JWT signing source", failure, StringComparison.Ordinal);
    }
}
