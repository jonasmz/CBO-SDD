using AuthService.Application.Abstractions;
using AuthService.Domain.Roles;
using AuthService.Domain.Security;

namespace AuthService.Application.Features.Bootstrap.BootstrapAdministrator;

public sealed class BootstrapAdministratorHandler(IUserDirectory userDirectory, IUserCreator userCreator)
{
    public async Task<BootstrapAdministratorOutcome> HandleAsync(
        BootstrapAdministratorCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await userDirectory.CountAsync(cancellationToken) > 0)
        {
            return BootstrapAdministratorOutcome.SkippedUsersExist;
        }

        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrWhiteSpace(command.Password))
        {
            return BootstrapAdministratorOutcome.SkippedNotConfigured;
        }

        if (PasswordPolicy.Validate(command.Password).Count > 0)
        {
            return BootstrapAdministratorOutcome.InvalidConfiguration;
        }

        var result = await userCreator.CreateAsync(
            command.Email,
            command.Password,
            [AppRoles.Administrator],
            isEnabled: true,
            cancellationToken);

        return result.Status == UserCreationStatus.Created
            ? BootstrapAdministratorOutcome.Created
            : BootstrapAdministratorOutcome.InvalidConfiguration;
    }
}
