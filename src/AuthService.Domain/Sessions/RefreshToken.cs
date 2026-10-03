namespace AuthService.Domain.Sessions;

public enum RefreshTokenState
{
    Active,
    Rotated,
    Revoked
}

public enum RefreshTokenRevocationReason
{
    Logout,
    ReuseDetected,
    UserDisabled,
    PasswordReset
}

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    private RefreshToken(
        Guid id,
        Guid familyId,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset sessionStartedAt,
        DateTimeOffset sessionExpiresAt)
    {
        Id = id;
        FamilyId = familyId;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        SessionStartedAt = sessionStartedAt;
        SessionExpiresAt = sessionExpiresAt;
        State = RefreshTokenState.Active;
    }

    public Guid Id { get; private set; }
    public Guid FamilyId { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset SessionStartedAt { get; private set; }
    public DateTimeOffset SessionExpiresAt { get; private set; }
    public RefreshTokenState State { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshTokenRevocationReason? RevokedReason { get; private set; }

    public static RefreshToken StartSession(
        Guid id,
        Guid familyId,
        Guid userId,
        string tokenHash,
        DateTimeOffset now,
        TimeSpan refreshTokenLifetime,
        TimeSpan sessionLifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (refreshTokenLifetime <= TimeSpan.Zero || sessionLifetime < refreshTokenLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshTokenLifetime));
        }

        var sessionExpiresAt = now.Add(sessionLifetime);
        return new RefreshToken(
            id,
            familyId,
            userId,
            tokenHash,
            now,
            Minimum(now.Add(refreshTokenLifetime), sessionExpiresAt),
            now,
            sessionExpiresAt);
    }

    public RefreshToken Rotate(
        DateTimeOffset now,
        Guid replacementId,
        string replacementTokenHash,
        TimeSpan refreshTokenLifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementTokenHash);
        if (!IsUsable(now))
        {
            throw new InvalidOperationException("Only an active, unexpired refresh token can be rotated.");
        }

        State = RefreshTokenState.Rotated;
        ReplacedByTokenId = replacementId;

        return new RefreshToken(
            replacementId,
            FamilyId,
            UserId,
            replacementTokenHash,
            now,
            Minimum(now.Add(refreshTokenLifetime), SessionExpiresAt),
            SessionStartedAt,
            SessionExpiresAt);
    }

    public void Revoke(RefreshTokenRevocationReason reason, DateTimeOffset now)
    {
        if (State == RefreshTokenState.Revoked)
        {
            return;
        }

        State = RefreshTokenState.Revoked;
        RevokedAt = now;
        RevokedReason = reason;
    }

    public bool IsUsable(DateTimeOffset now) =>
        State == RefreshTokenState.Active && now < ExpiresAt && now < SessionExpiresAt;

    private static DateTimeOffset Minimum(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;
}
