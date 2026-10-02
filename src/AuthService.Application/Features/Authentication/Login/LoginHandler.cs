using System.Net.Mail;
using AuthService.Application.Abstractions;
using Microsoft.Extensions.Logging;
namespace AuthService.Application.Features.Authentication.Login;
public sealed partial class LoginHandler(
    ICredentialVerifier verifier,
    IAccessTokenIssuer issuer,
    IRefreshTokenStore refreshTokens,
    TimeProvider timeProvider,
    ILogger<LoginHandler> logger)
{
 public async Task<LoginOutcome> HandleAsync(LoginRequest request, CancellationToken ct = default)
 {
  if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || !IsEmail(request.Email)) return new(LoginOutcomeKind.ValidationFailure);
  var facts=await verifier.CheckAsync(request.Email,request.Password,ct);
  if (!facts.UserFound || !facts.PasswordValid || !facts.IsEnabled || facts.IsLockedOut || facts.UserId is null) { Log.FailedSignIn(logger); return new(LoginOutcomeKind.InvalidCredentials); }
  var now=timeProvider.GetUtcNow();
  var token=issuer.Issue(facts.UserId.Value,facts.Roles);
  var refreshToken=await refreshTokens.CreateSessionAsync(facts.UserId.Value,now,ct);
  return new(LoginOutcomeKind.Success,new(token.Value,(long)(token.ExpiresAt-now).TotalSeconds,refreshToken.Value,refreshToken.ExpiresAt));
 }
 private static bool IsEmail(string value) { try { _=new MailAddress(value); return true; } catch { return false; } }
 private static partial class Log { [LoggerMessage(EventId=1, Level=LogLevel.Warning, Message="Failed sign-in")] public static partial void FailedSignIn(ILogger logger); }
}
