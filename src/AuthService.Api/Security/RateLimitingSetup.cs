using System.Threading.RateLimiting;
namespace AuthService.Api.Security;
public sealed class RateLimitOptions { public const string SectionName="RateLimit"; [System.ComponentModel.DataAnnotations.Range(1,1000)] public int PermitLimit {get;init;}=10; [System.ComponentModel.DataAnnotations.Range(1,60)] public int WindowMinutes {get;init;}=1; }
public static class RateLimitingSetup
{
 public const string AuthPolicy="auth";
 public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services,IConfiguration config)
 {
  services.AddOptions<RateLimitOptions>().Bind(config.GetSection(RateLimitOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
  var o=config.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()??new();
  services.AddRateLimiter(x=>{ x.RejectionStatusCode=429; x.AddPolicy(AuthPolicy,context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=o.PermitLimit,Window=TimeSpan.FromMinutes(o.WindowMinutes),QueueLimit=0,AutoReplenishment=true})); }); return services;
 }
}
