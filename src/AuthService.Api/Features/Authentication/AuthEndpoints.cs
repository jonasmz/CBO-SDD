using AuthService.Application.Features.Authentication.Login;
using AuthService.Application.Features.Authentication.Logout;
using AuthService.Application.Features.Authentication.RefreshToken;
using AuthService.Application.Features.Authentication.ResetPassword;

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
        }).RequireRateLimiting(AuthService.Api.Security.RateLimitingSetup.AuthPolicy);

        group.MapPost("/refresh", async (
            RefreshTokenRequest request,
            RefreshTokenHandler handler,
            CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request, cancellationToken);
            return outcome.Kind == RefreshTokenOutcomeKind.Success
                ? Results.Ok(outcome.Response)
                : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid refresh token");
        }).RequireRateLimiting(AuthService.Api.Security.RateLimitingSetup.AuthPolicy);

        group.MapPost("/logout", async (
            LogoutRequest request,
            LogoutHandler handler,
            CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(request, cancellationToken);
            return Results.NoContent();
        });
        group.MapPost("/reset-password", async (ResetPasswordRequest request, ResetPasswordHandler handler, CancellationToken cancellationToken) =>
        {
            var outcome = await handler.HandleAsync(request, cancellationToken);
            return outcome switch
            {
                ResetPasswordStatus.Succeeded => Results.NoContent(),
                ResetPasswordStatus.ValidationFailure => Results.ValidationProblem(new Dictionary<string,string[]> { ["newPassword"] = ["Password does not meet the rule."] }),
                _ => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid reset code")
            };
        }).RequireRateLimiting(AuthService.Api.Security.RateLimitingSetup.AuthPolicy);
    }
}
