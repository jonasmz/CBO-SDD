using AuthService.Domain.Roles;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Infrastructure.Identity;

public sealed class RoleSeeder(RoleManager<IdentityRole<Guid>> roleManager)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in AppRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not seed fixed role '{roleName}'.");
            }
        }
    }
}
