using AuthService.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Infrastructure.Identity;
public sealed class UserRoleStore(UserManager<ApplicationUser> users) : IUserRoleStore
{
    public async Task<IReadOnlyList<string>?> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : (await users.GetRolesAsync(user)).ToArray();
    }
    public async Task<bool> AddAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return false;
        return (await users.AddToRoleAsync(user, role)).Succeeded;
    }
    public async Task<bool> RemoveAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return false;
        return (await users.RemoveFromRoleAsync(user, role)).Succeeded;
    }
}
