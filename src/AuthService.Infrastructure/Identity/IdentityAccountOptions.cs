using System.ComponentModel.DataAnnotations;

namespace AuthService.Infrastructure.Identity;

public sealed class IdentityAccountOptions
{
    public const string SectionName = "Identity";

    [Range(1, 72)]
    public int ResetCodeHours { get; init; } = 24;

    [Range(1, int.MaxValue)]
    public int LockoutMaxFailedAttempts { get; init; } = 5;

    [Range(1, int.MaxValue)]
    public int LockoutMinutes { get; init; } = 15;
}
