using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using AuthService.Application.Abstractions;
using Microsoft.Extensions.Options;
namespace AuthService.Infrastructure.Tokens;
public sealed class JwtAccessTokenIssuer(SigningKeyProvider signing, IOptions<JwtOptions> options, TimeProvider time) : IAccessTokenIssuer { public AccessToken Issue(Guid userId,IReadOnlyCollection<string> roles) { var now=time.GetUtcNow(); var exp=now.AddMinutes(options.Value.AccessTokenMinutes); var claims=new List<Claim>{new(JwtRegisteredClaimNames.Sub,userId.ToString()),new(JwtRegisteredClaimNames.Iat,now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),ClaimValueTypes.Integer64)}; claims.AddRange(roles.Select(x=>new Claim("role",x))); var jwt=new JwtSecurityToken(options.Value.Issuer,options.Value.Audience,claims,notBefore:null,expires:exp.UtcDateTime,signingCredentials:signing.Credentials); return new(new JwtSecurityTokenHandler().WriteToken(jwt),exp); } }
