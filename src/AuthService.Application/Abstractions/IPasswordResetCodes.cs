namespace AuthService.Application.Abstractions;
public interface IPasswordResetCodes
{
    Task<PasswordResetCodeIssue?> IssueAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PasswordResetRedemption> RedeemAsync(string email, string code, string password, CancellationToken cancellationToken = default);
}
public sealed record PasswordResetCodeIssue(string Code, DateTimeOffset ExpiresAt);
public enum PasswordResetRedemption { Succeeded, InvalidCode }
