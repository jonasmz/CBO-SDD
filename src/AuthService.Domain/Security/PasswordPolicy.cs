namespace AuthService.Domain.Security;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static IReadOnlyList<string> Validate(string? password)
    {
        var violations = new List<string>();
        var candidate = password ?? string.Empty;

        if (candidate.Length < MinLength)
        {
            violations.Add("minimum-length");
        }

        if (!candidate.Any(char.IsUpper))
        {
            violations.Add("upper-case");
        }

        if (!candidate.Any(char.IsLower))
        {
            violations.Add("lower-case");
        }

        if (!candidate.Any(char.IsDigit))
        {
            violations.Add("digit");
        }

        return violations;
    }
}
