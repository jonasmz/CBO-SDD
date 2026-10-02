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

    private async Task<UserDirectoryEntry> ToEntryAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new UserDirectoryEntry(user.Id, user.Email ?? string.Empty, user.IsEnabled, roles.ToArray());
    }
}
