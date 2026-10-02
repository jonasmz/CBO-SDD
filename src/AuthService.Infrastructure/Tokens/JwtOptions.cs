using System.ComponentModel.DataAnnotations;
namespace AuthService.Infrastructure.Tokens;
public sealed class JwtOptions { public const string SectionName="Jwt"; [Required] public string Issuer {get;init;}=""; [Required] public string Audience {get;init;}=""; [Range(1,60)] public int AccessTokenMinutes {get;init;}=15; public SigningOptions Signing {get;init;}=new(); public sealed class SigningOptions { public string? PrivateKeyPemPath {get;init;} public string? CertificatePath {get;init;} public string? CertificatePassword {get;init;} public string[] PreviousPublicKeyPaths {get;init;}=[]; } }
