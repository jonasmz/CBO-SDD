using System.Security.Cryptography;
using AuthService.Infrastructure.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
namespace AuthService.Infrastructure.Tokens;
public sealed class SigningKeyProvider
{
 private readonly RsaSecurityKey _key; public SigningCredentials Credentials {get;} public string KeyId=>_key.KeyId!; public IReadOnlyList<JsonWebKey> PublicKeys {get;}
 public SigningKeyProvider(IOptions<JwtOptions> options, IHostEnvironment env)
 { var o=options.Value; var path=o.Signing.PrivateKeyPemPath; if(string.IsNullOrWhiteSpace(path)) { if(!env.IsDevelopment()) throw new InvalidOperationException("JWT signing key is required."); path="/data/dev-keys/signing.pem"; Directory.CreateDirectory(Path.GetDirectoryName(path)!); if(!File.Exists(path)){ using var r=RSA.Create(2048); File.WriteAllText(path,r.ExportRSAPrivateKeyPem()); } } if(!File.Exists(path)) throw new InvalidOperationException("JWT signing key is unreadable."); var rsa=RSA.Create(); rsa.ImportFromPem(File.ReadAllText(path)); if(rsa.KeySize<2048) throw new InvalidOperationException("JWT signing key must be at least 2048 bits."); _key=new RsaSecurityKey(rsa); _key.KeyId=Base64UrlEncoder.Encode(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())); Credentials=new SigningCredentials(_key,SecurityAlgorithms.RsaSha256); var p=rsa.ExportParameters(false); PublicKeys=[new JsonWebKey{Kty="RSA",N=Base64UrlEncoder.Encode(p.Modulus),E=Base64UrlEncoder.Encode(p.Exponent),Kid=_key.KeyId,Alg=SecurityAlgorithms.RsaSha256,Use="sig"}]; }
}
