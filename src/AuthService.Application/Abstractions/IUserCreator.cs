namespace AuthService.Application.Abstractions;

public interface IUserCreator
{
    Task<UserCreationResult> CreateAsync(
        string email,
        string password,
        IReadOnlyCollection<string> roles,
        bool isEnabled,
        CancellationToken cancellationToken = default);
}

public enum UserCreationStatus
{
    Created,
    EmailAlreadyExists,
    Rejected
}

public sealed record UserCreationResult(UserCreationStatus Status, IReadOnlyList<string> Reasons)
{
    public static UserCreationResult Created() => new(UserCreationStatus.Created, []);

    public static UserCreationResult EmailAlreadyExists() => new(UserCreationStatus.EmailAlreadyExists, []);

    public static UserCreationResult Rejected(IReadOnlyList<string> reasons) => new(UserCreationStatus.Rejected, reasons);
}
