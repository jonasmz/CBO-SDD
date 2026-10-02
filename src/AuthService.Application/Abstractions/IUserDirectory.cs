namespace AuthService.Application.Abstractions;

public interface IUserDirectory
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<UserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<UserDirectoryEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserDirectoryEntry>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);
    Task<int> CountEnabledInRoleAsync(string role, CancellationToken cancellationToken = default);
}

public sealed record UserDirectoryEntry(Guid Id, string Email, bool IsEnabled, IReadOnlyList<string> Roles, DateTimeOffset CreatedAt);
