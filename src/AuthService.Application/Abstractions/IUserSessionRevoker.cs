using AuthService.Domain.Sessions;

namespace AuthService.Application.Abstractions;

public interface IUserSessionRevoker
{
    Task RevokeAllAsync(
        Guid userId,
        RefreshTokenRevocationReason reason,
        CancellationToken cancellationToken = default);
}
