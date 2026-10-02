namespace AuthService.Application.Features.Users;
public sealed record UserDto(Guid Id, string Email, bool IsEnabled, IReadOnlyList<string> Roles, DateTimeOffset CreatedAt);
public sealed record UserListResponse(IReadOnlyList<UserDto> Items, int Total, int Skip, int Take);
public sealed record CreateUserRequest(string? Email, string? Password, IReadOnlyList<string>? Roles);
public enum CreateUserStatus { Created, ValidationFailure, Conflict }
public sealed record CreateUserOutcome(CreateUserStatus Status, UserDto? User = null, IReadOnlyList<string>? Errors = null);
internal static class UserMapping { internal static UserDto Map(Abstractions.UserDirectoryEntry u) => new(u.Id, u.Email, u.IsEnabled, u.Roles, u.CreatedAt); }
