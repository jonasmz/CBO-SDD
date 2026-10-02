using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace AuthService.Api.OpenApi;

public static class OpenApiSetup
{
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services, IHostEnvironment environment) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo { Title = "Central Authentication and Authorization Service", Version = "v1", Description = "Authentication, session renewal, current identity, and user administration API." };
                document.Servers?.Clear();
                if (environment.IsDevelopment())
                {
                    document.Components ??= new OpenApiComponents();
                    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "RS256 access token" };
                }
                else document.Components?.SecuritySchemes?.Clear();
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                operation.Description = metadata.OfType<IAllowAnonymous>().Any()
                    ? "Authentication: anonymous."
                    : metadata.OfType<IAuthorizeData>().Any()
                        ? "Authentication: required."
                        : "Authentication: anonymous.";
                if (environment.IsDevelopment() && metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer")] = []
                    });
                }
                return Task.CompletedTask;
            });
        });

    public static WebApplication MapApiOpenApi(this WebApplication app) { app.MapOpenApi(); return app; }
}
