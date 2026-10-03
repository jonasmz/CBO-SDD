using AuthService.Application.Abstractions;
using AuthService.Application.Features.Authentication.Login;
using AuthService.Domain.Sessions;
using Microsoft.Extensions.Logging;

namespace AuthService.Application.Features.Authentication.RefreshToken;

public sealed partial class RefreshTokenHandler(
    IRefreshTokenStore refreshTokens,
    IUserDirectory users,
    IAccessTokenIssuer accessTokens,
    TimeProvider timeProvider,
    ILogger<RefreshTokenHandler> logger)
{
    public async Task<RefreshTokenOutcome> HandleAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Unauthorized();
        }

        var stored = await refreshTokens.FindByTokenAsync(request.RefreshToken, cancellationToken);
        if (stored is null)
        {
            return Unauthorized();
        }

        var now = timeProvider.GetUtcNow();

        // Absolute expiry deliberately precedes state/reuse inspection.
        if (now >= stored.SessionExpiresAt)
        {
            Log.RefreshFailed(logger);
            return Unauthorized();
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsEnabled)
        {
            return Unauthorized();
        }

        if (now >= stored.ExpiresAt)
        {
            return Unauthorized();
        }

        if (stored.State != RefreshTokenState.Active)
        {
            await RevokeForReuseAsync(stored.FamilyId, now, cancellationToken);
            return Unauthorized();
        }

        var rotation = await refreshTokens.RotateAsync(stored, now, cancellationToken);
        if (rotation.Status != RefreshTokenRotationStatus.Succeeded || rotation.Token is null)
        {
            if (rotation.Status == RefreshTokenRotationStatus.ConcurrentReuse)
            {
                await RevokeForReuseAsync(stored.FamilyId, now, cancellationToken);
            }

            return Unauthorized();
        }

        var accessToken = accessTokens.Issue(user.Id, user.Roles);
        return new RefreshTokenOutcome(
            RefreshTokenOutcomeKind.Success,
            new LoginResponse(
                accessToken.Value,
                (long)(accessToken.ExpiresAt - now).TotalSeconds,
                rotation.Token.Value,
                rotation.Token.ExpiresAt));
    }

    private async Task RevokeForReuseAsync(
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeFamilyAsync(
            familyId,
            RefreshTokenRevocationReason.ReuseDetected,
            now,
            cancellationToken);
        Log.ReuseDetected(logger, familyId);
    }

    private static RefreshTokenOutcome Unauthorized() =>
        new(RefreshTokenOutcomeKind.Unauthorized);

    private static partial class Log
    {
        [LoggerMessage(EventId = 20, Level = LogLevel.Warning, Message = "Refresh-token reuse detected for session family {FamilyId}")]
        public static partial void ReuseDetected(ILogger logger, Guid familyId);

        [LoggerMessage(EventId = 21, Level = LogLevel.Information, Message = "Refresh rejected because the session expired")]
        public static partial void RefreshFailed(ILogger logger);
    }
}
