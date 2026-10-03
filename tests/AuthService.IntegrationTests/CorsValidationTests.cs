using static AuthService.IntegrationTests.DeploymentTestSupport;

namespace AuthService.IntegrationTests;

public sealed class CorsValidationTests
{
    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("https://app.example.test/")]
    [InlineData("https://app.example.test/path")]
    [InlineData("https://app.example.test?q=1")]
    [InlineData("https://app.example.test#fragment")]
    [InlineData("https://user@app.example.test")]
    [InlineData("ftp://app.example.test")]
    public void Invalid_production_origin_fails_startup_and_is_not_normalized(string origin)
    {
        using var factory = Production(("Cors:AllowedOrigins:0", origin));

        var failure = AllMessages(StartFails(factory));

        Assert.Contains("CORS origin", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_without_any_configured_origin_fails_startup()
    {
        using var factory = Production(("Cors:AllowedOrigins:0", null));

        Assert.Contains("at least one CORS origin", AllMessages(StartFails(factory)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://app.example.test")]
    [InlineData("http://localhost:4200")]
    [InlineData("https://app.example.test:8443")]
    public async Task Valid_explicit_production_origin_is_accepted_exactly_as_configured(string origin)
    {
        using var factory = Production(("Cors:AllowedOrigins:0", origin));
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
