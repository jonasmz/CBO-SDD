using AuthService.Domain.Sessions;

namespace AuthService.Application.Abstractions;

public interface IRefreshTokenStore
{
    Task<IssuedRefreshToken> CreateSessionAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<RefreshToken?> FindByTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default);

    Task<RefreshTokenRotation> RotateAsync(
        RefreshToken current,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task RevokeFamilyAsync(
        Guid familyId,
        RefreshTokenRevocationReason reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt);

public enum RefreshTokenRotationStatus
{
    Succeeded,
    NotUsable,
    ConcurrentReuse
}

public sealed record RefreshTokenRotation(
    RefreshTokenRotationStatus Status,
    IssuedRefreshToken? Token = null);
