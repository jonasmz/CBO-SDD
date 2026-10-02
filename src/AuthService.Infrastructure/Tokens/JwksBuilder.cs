namespace AuthService.Infrastructure.Tokens;

public sealed class JwksBuilder(SigningKeyProvider keys)
{
    public object Build() => new
    {
        keys = keys.PublicKeys.Select(key => new
        {
            kty = key.Kty,
            use = key.Use,
            alg = key.Alg,
            kid = key.Kid,
            n = key.N,
            e = key.E
        })
    };
}
