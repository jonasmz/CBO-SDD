using Scalar.AspNetCore;
namespace AuthService.Api.OpenApi;
public static class ScalarSetup
{
 public static void MapApiScalar(this WebApplication app)
 {
  app.MapScalarApiReference("/scalar", options =>
  {
   options.Title = "Auth API";
   if (!app.Environment.IsDevelopment())
   {
    options.HideTestRequestButton = true;
    options.HideClientButton = true;
   }
  });
 }
}
