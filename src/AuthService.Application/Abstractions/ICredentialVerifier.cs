namespace AuthService.Application.Abstractions;
public interface ICredentialVerifier { Task<CredentialCheck> CheckAsync(string email, string password, CancellationToken cancellationToken = default); }
public sealed record CredentialCheck(bool UserFound, bool PasswordValid, bool IsLockedOut, bool IsEnabled, Guid? UserId, string? Email, IReadOnlyList<string> Roles);
