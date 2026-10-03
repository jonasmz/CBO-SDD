using AuthService.Application.Abstractions;
using AuthService.Domain.Sessions;

namespace AuthService.Application.Features.Authentication.Logout;

public sealed class LogoutHandler(IRefreshTokenStore refreshTokens, TimeProvider timeProvider)
{
    public async Task HandleAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return;
        }

        var stored = await refreshTokens.FindByTokenAsync(request.RefreshToken, cancellationToken);
        if (stored is null)
        {
            return;
        }

        await refreshTokens.RevokeFamilyAsync(
            stored.FamilyId,
            RefreshTokenRevocationReason.Logout,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
