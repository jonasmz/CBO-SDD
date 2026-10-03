namespace AuthService.Application.Abstractions;
public interface IAccessTokenIssuer { AccessToken Issue(Guid userId, IReadOnlyCollection<string> roles); }
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
