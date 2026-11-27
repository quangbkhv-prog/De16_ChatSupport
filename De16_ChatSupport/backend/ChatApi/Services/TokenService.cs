using System.Text;
using ChatSupport.ChatApi.Options;
using ChatSupport.Shared.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ChatSupport.ChatApi.Services;

public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAt) CreateToken(Participant participant, Guid? conversationId = null)
    {
        var role = participant.Role == ParticipantRole.Agent ? Actor.AgentRole : Actor.CustomerRole;
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpiresMinutes);

        var claims = new Dictionary<string, object>
        {
            ["sub"] = participant.Id.ToString(),
            ["name"] = participant.DisplayName,
            ["role"] = role
        };
        if (conversationId is { } cid) claims["cid"] = cid.ToString();

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret)),
                SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
