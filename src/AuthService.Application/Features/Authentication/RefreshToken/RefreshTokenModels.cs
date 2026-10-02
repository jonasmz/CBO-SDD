using AuthService.Application.Features.Authentication.Login;

namespace AuthService.Application.Features.Authentication.RefreshToken;

public sealed record RefreshTokenRequest(string? RefreshToken);

public enum RefreshTokenOutcomeKind
{
    Success,
    Unauthorized
}

public sealed record RefreshTokenOutcome(
    RefreshTokenOutcomeKind Kind,
    LoginResponse? Response = null);
