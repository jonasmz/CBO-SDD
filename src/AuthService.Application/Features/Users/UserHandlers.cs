using System.Net.Mail;
using AuthService.Application.Abstractions;
using AuthService.Domain.Roles;
using AuthService.Domain.Security;
using AuthService.Domain.Sessions;
using Microsoft.Extensions.Logging;

namespace AuthService.Application.Features.Users;
public sealed class ListUsersHandler(IUserDirectory users)
{
    public async Task<UserListResponse?> HandleAsync(int skip, int take, CancellationToken ct = default)
    {
        if (skip < 0 || take is < 1 or > 200) return null;
        var entries = await users.ListAsync(skip, take, ct);
        return new UserListResponse(entries.Select(UserMapping.Map).ToArray(), await users.CountAsync(ct), skip, take);
    }
}
public sealed class GetUserHandler(IUserDirectory users)
{
    public async Task<UserDto?> HandleAsync(Guid id, CancellationToken ct = default) { var u = await users.GetByIdAsync(id, ct); return u is null ? null : UserMapping.Map(u); }
}
public sealed class CreateUserHandler(IUserDirectory users, IUserCreator creator)
{
    public async Task<CreateUserOutcome> HandleAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || !IsEmail(request.Email) || string.IsNullOrWhiteSpace(request.Password) || request.Roles is null || request.Roles.Count == 0)
            return new(CreateUserStatus.ValidationFailure, Errors: ["Invalid user details."]);
        var roles = new List<string>();
        foreach (var role in request.Roles) { if (!AppRoles.TryParse(role, out var canonical)) return new(CreateUserStatus.ValidationFailure, Errors: ["Unsupported role."]); if (!roles.Contains(canonical, StringComparer.Ordinal)) roles.Add(canonical); }
        var violations = PasswordPolicy.Validate(request.Password);
        if (violations.Count > 0) return new(CreateUserStatus.ValidationFailure, Errors: violations);
        if (await users.FindByEmailAsync(request.Email, ct) is not null) return new(CreateUserStatus.Conflict);
        var created = await creator.CreateAsync(request.Email, request.Password, roles, true, ct);
        if (created.Status == UserCreationStatus.EmailAlreadyExists) return new(CreateUserStatus.Conflict);
        if (created.Status != UserCreationStatus.Created) return new(CreateUserStatus.ValidationFailure, Errors: created.Reasons);
        var user = await users.FindByEmailAsync(request.Email, ct);
        return user is null ? new(CreateUserStatus.ValidationFailure) : new(CreateUserStatus.Created, UserMapping.Map(user));
    }
    private static bool IsEmail(string value) { try { _ = new MailAddress(value); return true; } catch { return false; } }
}
public enum StateChangeStatus { Succeeded, NotFound, Conflict }
public sealed partial class SetUserEnabledHandler(IUserDirectory users, IUserStateStore states, IUserSessionRevoker sessions, ILogger<SetUserEnabledHandler> logger)
{
    public async Task<StateChangeStatus> HandleAsync(Guid actorId, Guid targetId, bool enabled, CancellationToken ct = default)
    {
        var target = await users.GetByIdAsync(targetId, ct); if (target is null) return StateChangeStatus.NotFound;
        if (!enabled && target.IsEnabled && target.Roles.Contains(AppRoles.Administrator, StringComparer.Ordinal) && await users.CountEnabledInRoleAsync(AppRoles.Administrator, ct) <= 1) return StateChangeStatus.Conflict;
        if (!await states.SetEnabledAsync(targetId, enabled, ct)) return StateChangeStatus.NotFound;
        if (!enabled) await sessions.RevokeAllAsync(targetId, RefreshTokenRevocationReason.UserDisabled, ct);
        Log.Change(logger, actorId, targetId, enabled); return StateChangeStatus.Succeeded;
    }
    private static partial class Log { [LoggerMessage(EventId=30, Level=LogLevel.Information, Message="User enabled state changed by {ActorId} for {TargetId}: {Enabled}")] public static partial void Change(ILogger logger, Guid actorId, Guid targetId, bool enabled); }
}
public enum RoleChangeStatus { Succeeded, NotFound, ValidationFailure, Conflict }
public sealed partial class ChangeUserRoleHandler(IUserDirectory users, IUserRoleStore roles, ILogger<ChangeUserRoleHandler> logger)
{
    public async Task<RoleChangeStatus> AssignAsync(Guid actorId, Guid targetId, string role, CancellationToken ct = default)
    {
        if (!AppRoles.TryParse(role, out var canonical)) return RoleChangeStatus.ValidationFailure;
        var current = await roles.GetRolesAsync(targetId, ct); if (current is null) return RoleChangeStatus.NotFound;
        if (!current.Contains(canonical, StringComparer.Ordinal) && !await roles.AddAsync(targetId, canonical, ct)) return RoleChangeStatus.NotFound;
        Log.Change(logger, actorId, targetId, canonical, true); return RoleChangeStatus.Succeeded;
    }
    public async Task<RoleChangeStatus> RemoveAsync(Guid actorId, Guid targetId, string role, CancellationToken ct = default)
    {
        if (!AppRoles.TryParse(role, out var canonical)) return RoleChangeStatus.ValidationFailure;
        var target = await users.GetByIdAsync(targetId, ct); if (target is null) return RoleChangeStatus.NotFound;
        if (!target.Roles.Contains(canonical, StringComparer.Ordinal)) return RoleChangeStatus.Succeeded;
        if (target.Roles.Count <= 1) return RoleChangeStatus.Conflict;
        if (target.IsEnabled && canonical == AppRoles.Administrator && await users.CountEnabledInRoleAsync(AppRoles.Administrator, ct) <= 1) return RoleChangeStatus.Conflict;
        if (!await roles.RemoveAsync(targetId, canonical, ct)) return RoleChangeStatus.NotFound;
        Log.Change(logger, actorId, targetId, canonical, false); return RoleChangeStatus.Succeeded;
    }
    private static partial class Log { [LoggerMessage(EventId=31, Level=LogLevel.Information, Message="Role {Role} assignment={Assigned} by {ActorId} for {TargetId}")] public static partial void Change(ILogger logger, Guid actorId, Guid targetId, string role, bool assigned); }
}
public sealed partial class IssuePasswordResetHandler(IUserDirectory users, IPasswordResetCodes codes, ILogger<IssuePasswordResetHandler> logger)
{
    public async Task<PasswordResetCodeIssue?> HandleAsync(Guid actorId, Guid targetId, CancellationToken ct = default)
    {
        if (await users.GetByIdAsync(targetId, ct) is null) return null;
        var code = await codes.IssueAsync(targetId, ct); if (code is not null) Log.Issued(logger, actorId, targetId); return code;
    }
    private static partial class Log { [LoggerMessage(EventId=32, Level=LogLevel.Information, Message="Password reset issued by {ActorId} for {TargetId}")] public static partial void Issued(ILogger logger, Guid actorId, Guid targetId); }
}
