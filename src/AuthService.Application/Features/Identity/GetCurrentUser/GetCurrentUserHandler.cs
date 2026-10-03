using AuthService.Application.Abstractions;
namespace AuthService.Application.Features.Identity.GetCurrentUser;
public sealed record CurrentUserResponse(Guid Id, string Email, IReadOnlyList<string> Roles);
public sealed class GetCurrentUserHandler(IUserDirectory users)
{
    public async Task<CurrentUserResponse?> HandleAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null || !user.IsEnabled ? null : new CurrentUserResponse(user.Id, user.Email, user.Roles);
    }
}
