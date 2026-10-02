using System.Security.Claims;
using AuthService.Application.Features.Identity.GetCurrentUser;
namespace AuthService.Api.Features.Identity;
public static class MeEndpoints
{
 public static void MapMe(this IEndpointRouteBuilder app) => app.MapGet("/api/me", async (ClaimsPrincipal user, GetCurrentUserHandler handler, CancellationToken ct) =>
 {
  if (!Guid.TryParse(user.FindFirstValue("sub"), out var id)) return Results.Unauthorized();
  var result=await handler.HandleAsync(id,ct); return result is null ? Results.Unauthorized() : Results.Ok(result);
 }).RequireAuthorization();
}
