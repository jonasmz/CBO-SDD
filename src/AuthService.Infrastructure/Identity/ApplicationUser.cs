using Microsoft.AspNetCore.Identity;

namespace AuthService.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
