namespace AuthService.Application.Abstractions;

public interface IUserDirectory
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<UserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<UserDirectoryEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record UserDirectoryEntry(Guid Id, string Email, bool IsEnabled, IReadOnlyList<string> Roles);
