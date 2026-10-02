using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AuthService.Api.Features.Metadata;
public static class HealthEndpoint
{
 public static void MapHealth(this IEndpointRouteBuilder app)=>app.MapGet("/health",async(AuthDbContext db,CancellationToken ct)=>await db.Database.CanConnectAsync(ct)?Results.Text("Healthy"):Results.Text("Unhealthy",statusCode:503)).AllowAnonymous();
}
