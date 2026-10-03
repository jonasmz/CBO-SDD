namespace AuthService.Application.Abstractions;
public interface IUserRoleStore
{
    Task<IReadOnlyList<string>?> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> AddAsync(Guid userId, string role, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid userId, string role, CancellationToken cancellationToken = default);
}
