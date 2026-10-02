namespace AuthService.Application.Abstractions;
public interface IUserStateStore { Task<bool> SetEnabledAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default); }
