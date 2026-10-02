namespace AuthService.Api.OpenApi;
public static class OpenApiSetup
{
 public static IServiceCollection AddApiOpenApi(this IServiceCollection services) => services.AddOpenApi();
 public static WebApplication MapApiOpenApi(this WebApplication app) { app.MapOpenApi(); return app; }
}
