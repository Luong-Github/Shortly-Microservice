using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IdentityService.Services
{
    public class JwtTokenGenerator
    {
        private readonly IConfiguration _configurateion;

        public JwtTokenGenerator(IConfiguration configuration)
        {
            _configurateion = configuration;
        }

        public string GenerateToken(string userId, string email, string[] roles)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configurateion["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var audience = _configurateion["Jwt:Audiences:0"] ?? "urlshortent_api";

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var token = new JwtSecurityToken(
                _configurateion["Jwt:Issuer"],
                audience,
                claims,
                expires: DateTime.Now.AddMinutes(5),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
