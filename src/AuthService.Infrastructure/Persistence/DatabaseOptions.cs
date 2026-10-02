using System.ComponentModel.DataAnnotations;

namespace AuthService.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string Path { get; init; } = "/data/auth.db";

    [Required]
    public string DataProtectionKeysPath { get; init; } = "/data/keys";

    public bool MigrateOnStartup { get; init; }
}
