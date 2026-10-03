using AuthService.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AuthService.Infrastructure.Identity;
public sealed class PasswordResetCodes(UserManager<ApplicationUser> users, IOptions<IdentityAccountOptions> options, TimeProvider timeProvider) : IPasswordResetCodes
{
    public async Task<PasswordResetCodeIssue?> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : new PasswordResetCodeIssue(await users.GeneratePasswordResetTokenAsync(user), timeProvider.GetUtcNow().AddHours(options.Value.ResetCodeHours));
    }
    public async Task<PasswordResetRedemption> RedeemAsync(string email, string code, string password, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null) return PasswordResetRedemption.InvalidCode;
        return (await users.ResetPasswordAsync(user, code, password)).Succeeded ? PasswordResetRedemption.Succeeded : PasswordResetRedemption.InvalidCode;
    }
}
