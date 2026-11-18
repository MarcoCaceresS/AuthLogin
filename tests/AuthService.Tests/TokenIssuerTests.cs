using AuthService.Security;
using AuthService.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AuthService.Tests;

public sealed class TokenIssuerTests
{
    private readonly FakeTime _time = new();
    private readonly TokenIssuer _issuer;

    public TokenIssuerTests() =>
        _issuer = new TokenIssuer(Options.Create(new AuthOptions { JwtSecret = ApiFactory.TestSecret }), _time);

    [Fact]
    public void Token_tiene_claims_del_contrato()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var issued = _issuer.Issue(userId, "funcionario", sessionId);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(issued.AccessToken);

        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(userId.ToString(), jwt.Subject);
        Assert.Equal("funcionario", jwt.GetClaim("rol").Value);
        Assert.Equal(sessionId.ToString(), jwt.GetClaim("sid").Value);
        Assert.Equal(issued.Jti, jwt.Id);
        Assert.Equal(_time.Now.ToUnixTimeSeconds(), jwt.GetPayloadValue<long>("iat"));
    }

    [Fact]
    public void Exp_menos_iat_es_7_dias()
    {
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(
            _issuer.Issue(Guid.NewGuid(), "funcionario", Guid.NewGuid()).AccessToken);

        Assert.Equal(604800, jwt.GetPayloadValue<long>("exp") - jwt.GetPayloadValue<long>("iat"));
    }

    [Fact]
    public void Dos_tokens_tienen_jti_distinto()
    {
        var a = _issuer.Issue(Guid.NewGuid(), "funcionario", Guid.NewGuid());
        var b = _issuer.Issue(Guid.NewGuid(), "funcionario", Guid.NewGuid());

        Assert.NotEqual(a.Jti, b.Jti);
    }

    [Fact]
    public void HashJti_es_sha256_hex_de_64()
    {
        var hash = TokenIssuer.HashJti("abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }
}
