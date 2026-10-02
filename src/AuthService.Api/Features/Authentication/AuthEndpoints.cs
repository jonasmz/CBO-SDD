using AuthService.Application.Features.Authentication.Login;
using AuthService.Application.Features.Authentication.Logout;
using AuthService.Application.Features.Authentication.RefreshToken;

namespace AuthService.Api.Features.Authentication;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous();

        group.MapPost("/login", async (LoginRequest request, LoginHandler handler, CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request, cancellationToken);
            return outcome.Kind switch
            {
                LoginOutcomeKind.Success => Results.Ok(outcome.Response),
                LoginOutcomeKind.ValidationFailure => Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["credentials"] = ["Invalid credentials."] }),
                _ => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials")
            };
        });

        group.MapPost("/refresh", async (
            RefreshTokenRequest request,
            RefreshTokenHandler handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request, cancellationToken);
            return outcome.Kind == RefreshTokenOutcomeKind.Success
                ? Results.Ok(outcome.Response)
                : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid refresh token");
        });

        group.MapPost("/logout", async (
            LogoutRequest request,
            LogoutHandler handler,
            CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(request, cancellationToken);
            return Results.NoContent();
        });
    }
}
