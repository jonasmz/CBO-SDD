using AuthService.Application.Abstractions;
using AuthService.Domain.Security;
using AuthService.Domain.Sessions;
using Microsoft.Extensions.Logging;
namespace AuthService.Application.Features.Authentication.ResetPassword;
public sealed record ResetPasswordRequest(string? Email, string? ResetCode, string? NewPassword);
public enum ResetPasswordStatus { Succeeded, ValidationFailure, Unauthorized }
public sealed partial class ResetPasswordHandler(IUserDirectory users, IPasswordResetCodes codes, IUserSessionRevoker sessions, ILogger<ResetPasswordHandler> logger)
{
 public async Task<ResetPasswordStatus> HandleAsync(ResetPasswordRequest request, CancellationToken ct=default)
 {
  var violations=PasswordPolicy.Validate(request.NewPassword); if(violations.Count>0) return ResetPasswordStatus.ValidationFailure;
  if(string.IsNullOrWhiteSpace(request.Email)||string.IsNullOrWhiteSpace(request.ResetCode)) return ResetPasswordStatus.Unauthorized;
  var user=await users.FindByEmailAsync(request.Email,ct); if(user is null||!user.IsEnabled) return ResetPasswordStatus.Unauthorized;
  if(await codes.RedeemAsync(request.Email,request.ResetCode,request.NewPassword!,ct)!=PasswordResetRedemption.Succeeded) return ResetPasswordStatus.Unauthorized;
  await sessions.RevokeAllAsync(user.Id,RefreshTokenRevocationReason.PasswordReset,ct); Log.Redeemed(logger,user.Id); return ResetPasswordStatus.Succeeded;
 }
 private static partial class Log { [LoggerMessage(EventId=33,Level=LogLevel.Information,Message="Password reset redeemed for {UserId}")] public static partial void Redeemed(ILogger logger,Guid userId); }
}
