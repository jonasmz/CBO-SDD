namespace AuthService.Api.Security;
public sealed class CorsOptions { public const string SectionName="Cors"; public string[] AllowedOrigins {get;init;}=[]; }
public static class CorsSetup
{
 // Exact, non-normalizing: scheme://host[:port] only.
 private static readonly System.Text.RegularExpressions.Regex OriginShape=new(@"^https?://[^/?#@\s]+\z",System.Text.RegularExpressions.RegexOptions.CultureInvariant);
 public static bool IsExplicitOrigin(string? origin)=>origin is not null && !origin.Contains('*') && OriginShape.IsMatch(origin) && Uri.TryCreate(origin,UriKind.Absolute,out var uri) && !string.IsNullOrEmpty(uri.Host);
 public const string Policy="ConfiguredOrigins";
 public static IServiceCollection AddConfiguredCors(this IServiceCollection services,IConfiguration config,IHostEnvironment env)
 {
  services.AddOptions<CorsOptions>().Bind(config.GetSection(CorsOptions.SectionName))
   .Validate(o=>env.IsDevelopment()
      ? o.AllowedOrigins.All(x=>Uri.TryCreate(x,UriKind.Absolute,out _))
      : o.AllowedOrigins.Length>0 && o.AllowedOrigins.All(IsExplicitOrigin),
    env.IsDevelopment() ? "CORS origins must be absolute."
      : "Production requires at least one CORS origin; each must be an absolute http/https origin (scheme://host[:port]) with no wildcard, userinfo, path, query, fragment or trailing slash.")
   .ValidateOnStart();
  services.AddCors(o=>o.AddPolicy(Policy,p=>{var origins=config.GetSection(CorsOptions.SectionName).Get<CorsOptions>()?.AllowedOrigins??[]; if(env.IsDevelopment()&&origins.Length==0) origins=["http://localhost:4200"]; if(origins.Length>0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod(); })); return services;
 }
}
