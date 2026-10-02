namespace AuthService.Application.Features.Authentication.Login;
public sealed record LoginRequest(string? Email, string? Password);
public sealed record LoginResponse(string AccessToken, long ExpiresInSeconds);
public enum LoginOutcomeKind { Success, ValidationFailure, InvalidCredentials }
public sealed record LoginOutcome(LoginOutcomeKind Kind, LoginResponse? Response = null);
