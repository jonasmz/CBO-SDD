namespace AuthService.Api.Hosting;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminEmail { get; init; }

    public string? AdminPassword { get; init; }
}
