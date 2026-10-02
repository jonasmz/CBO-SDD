namespace AuthService.Api.Security;
public sealed class CorsOptions { public const string SectionName="Cors"; public string[] AllowedOrigins {get;init;}=[]; }
public static class CorsSetup
{
 public const string Policy="ConfiguredOrigins";
 public static IServiceCollection AddConfiguredCors(this IServiceCollection services,IConfiguration config,IHostEnvironment env)
 {
  services.AddOptions<CorsOptions>().Bind(config.GetSection(CorsOptions.SectionName)).Validate(o=>o.AllowedOrigins.All(x=>Uri.TryCreate(x,UriKind.Absolute,out _)),"CORS origins must be absolute.").ValidateOnStart();
  services.AddCors(o=>o.AddPolicy(Policy,p=>{var origins=config.GetSection(CorsOptions.SectionName).Get<CorsOptions>()?.AllowedOrigins??[]; if(env.IsDevelopment()&&origins.Length==0) origins=["http://localhost:4200"]; if(origins.Length>0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod(); })); return services;
 }
}
