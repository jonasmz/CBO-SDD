namespace AuthService.Domain.Roles;

public static class AppRoles
{
    public const string Administrator = nameof(Administrator);
    public const string Manager = nameof(Manager);
    public const string Operator = nameof(Operator);

    public static IReadOnlyList<string> All { get; } = [Administrator, Manager, Operator];

    public static bool TryParse(string? value, out string canonicalName)
    {
        canonicalName = value switch
        {
            Administrator => Administrator,
            Manager => Manager,
            Operator => Operator,
            _ => string.Empty
        };

        return canonicalName.Length > 0;
    }
}
