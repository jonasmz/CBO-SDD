using AuthService.Application.Abstractions;
using AuthService.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Infrastructure.Identity;

public sealed class UserCreator(UserManager<ApplicationUser> userManager, AuthDbContext dbContext) : IUserCreator
{
    public async Task<UserCreationResult> CreateAsync(
        string email,
        string password,
        IReadOnlyCollection<string> roles,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            IsEnabled = isEnabled,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return createResult.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.DuplicateEmail))
                ? UserCreationResult.EmailAlreadyExists()
                : UserCreationResult.Rejected(createResult.Errors.Select(error => error.Code).ToArray());
        }

        var rolesResult = await userManager.AddToRolesAsync(user, roles);
        if (!rolesResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return UserCreationResult.Rejected(rolesResult.Errors.Select(error => error.Code).ToArray());
        }

        await transaction.CommitAsync(cancellationToken);
        return UserCreationResult.Created();
    }
}
