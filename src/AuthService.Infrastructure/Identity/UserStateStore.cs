using AuthService.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Infrastructure.Identity;
public sealed class UserStateStore(UserManager<ApplicationUser> users) : IUserStateStore
{
    public async Task<bool> SetEnabledAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return false;
        user.IsEnabled = enabled;
        return (await users.UpdateAsync(user)).Succeeded;
    }
}
