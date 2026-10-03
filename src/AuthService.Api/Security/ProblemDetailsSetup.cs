namespace AuthService.Api.Security;
public static class ProblemDetailsSetup
{
 public static IServiceCollection AddApiProblemDetails(this IServiceCollection services) { services.AddProblemDetails(); return services; }
 public static WebApplication UseApiProblemDetails(this WebApplication app) { if(!app.Environment.IsDevelopment()) app.UseExceptionHandler(); app.UseStatusCodePages(); return app; }
}
