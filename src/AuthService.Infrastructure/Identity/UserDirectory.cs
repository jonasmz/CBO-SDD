using AuthService.Application.Abstractions;
using AuthService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Infrastructure.Identity;

public sealed class UserDirectory(UserManager<ApplicationUser> userManager, AuthDbContext dbContext) : IUserDirectory
{
    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        dbContext.Users.CountAsync(cancellationToken);

    public async Task<UserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is null ? null : await ToEntryAsync(user);
    }

    public async Task<UserDirectoryEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return user is null ? null : await ToEntryAsync(user);
    }

    public async Task<IReadOnlyList<UserDirectoryEntry>> ListAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        var users = await dbContext.Users.OrderBy(user => user.Email).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return await Task.WhenAll(users.Select(ToEntryAsync));
    }

    public Task<int> CountEnabledInRoleAsync(string role, CancellationToken cancellationToken = default) =>
        (from user in dbContext.Users
         join membership in dbContext.UserRoles on user.Id equals membership.UserId
         join identityRole in dbContext.Roles on membership.RoleId equals identityRole.Id
         where user.IsEnabled && identityRole.Name == role
         select user.Id).CountAsync(cancellationToken);

    private async Task<UserDirectoryEntry> ToEntryAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new UserDirectoryEntry(user.Id, user.Email ?? string.Empty, user.IsEnabled, roles.ToArray(), user.CreatedAt);
    }
}
