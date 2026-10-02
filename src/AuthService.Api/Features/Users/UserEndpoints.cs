using System.Security.Claims;
using AuthService.Api.Security;
using AuthService.Application.Features.Users;
namespace AuthService.Api.Features.Users;
public static class UserEndpoints
{
 public static void MapUsers(this IEndpointRouteBuilder app)
 {
  var group=app.MapGroup("/api/users").RequireAuthorization(AuthorizationPolicies.RequireAdministrator);
  group.MapGet("", async (int? skip, int? take, ListUsersHandler h, CancellationToken ct) => { var r=await h.HandleAsync(skip??0,take??50,ct); return r is null ? Results.ValidationProblem(new Dictionary<string,string[]>{{"paging",["Invalid paging."]}}) : Results.Ok(r); });
  group.MapGet("/{id:guid}", async (Guid id, GetUserHandler h, CancellationToken ct) => { var r=await h.HandleAsync(id,ct); return r is null ? Results.NotFound() : Results.Ok(r); });
  group.MapPost("", async (CreateUserRequest r, CreateUserHandler h, CancellationToken ct) => { var o=await h.HandleAsync(r,ct); return o.Status switch { CreateUserStatus.Created => Results.Created($"/api/users/{o.User!.Id}",o.User), CreateUserStatus.Conflict => Results.Conflict(), _ => Results.ValidationProblem(new Dictionary<string,string[]>{{"user",o.Errors?.ToArray()??["Invalid user details."]}})}; });
  group.MapPost("/{id:guid}/enable", (Guid id, ClaimsPrincipal p, SetUserEnabledHandler h, CancellationToken ct) => ChangeState(id,p,true,h,ct));
  group.MapPost("/{id:guid}/disable", (Guid id, ClaimsPrincipal p, SetUserEnabledHandler h, CancellationToken ct) => ChangeState(id,p,false,h,ct));
  group.MapPost("/{id:guid}/password-reset", async (Guid id, ClaimsPrincipal p, IssuePasswordResetHandler h, CancellationToken ct) => { if(!Actor(p,out var a)) return Results.Unauthorized(); var r=await h.HandleAsync(a,id,ct); return r is null ? Results.NotFound() : Results.Ok(new { resetCode=r.Code, expiresAt=r.ExpiresAt}); });
  group.MapPut("/{id:guid}/roles/{role}", (Guid id,string role,ClaimsPrincipal p,ChangeUserRoleHandler h,CancellationToken ct)=>ChangeRole(id,role,p,h,true,ct));
  group.MapDelete("/{id:guid}/roles/{role}", (Guid id,string role,ClaimsPrincipal p,ChangeUserRoleHandler h,CancellationToken ct)=>ChangeRole(id,role,p,h,false,ct));
 }
 private static async Task<IResult> ChangeState(Guid id,ClaimsPrincipal p,bool enabled,SetUserEnabledHandler h,CancellationToken ct) { if(!Actor(p,out var a)) return Results.Unauthorized(); var r=await h.HandleAsync(a,id,enabled,ct); return r switch { StateChangeStatus.Succeeded=>Results.NoContent(),StateChangeStatus.NotFound=>Results.NotFound(),_=>Results.Conflict()}; }
 private static async Task<IResult> ChangeRole(Guid id,string role,ClaimsPrincipal p,ChangeUserRoleHandler h,bool add,CancellationToken ct) { if(!Actor(p,out var a)) return Results.Unauthorized(); var r=add?await h.AssignAsync(a,id,role,ct):await h.RemoveAsync(a,id,role,ct); return r switch { RoleChangeStatus.Succeeded=>Results.NoContent(),RoleChangeStatus.NotFound=>Results.NotFound(),RoleChangeStatus.ValidationFailure=>Results.ValidationProblem(new Dictionary<string,string[]>{{"role",["Unsupported role."]}}),_=>Results.Conflict()}; }
 private static bool Actor(ClaimsPrincipal p,out Guid id)=>Guid.TryParse(p.FindFirstValue("sub"),out id);
}
