using System.ComponentModel.DataAnnotations;

namespace AuthService.Infrastructure.Sessions;

public sealed class SessionOptions
{
    public const string SectionName = "Session";

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 7;

    [Range(1, 365)]
    public int SessionMaxDays { get; init; } = 30;
}
