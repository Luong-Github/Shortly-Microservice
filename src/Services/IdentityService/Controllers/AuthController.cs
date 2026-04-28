using IdentityService.Models;
using IdentityService.Models.Requests;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Security.Claims;
using MediatR;
using IdentityService.Commands;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace IdentityService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _config;
        private readonly IMediator _mediator;
        private readonly IdentityService.Data.AppIdentityDbContext _dbContext;

        public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IConfiguration config, IMediator mediator, IdentityService.Data.AppIdentityDbContext dbContext)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _mediator = mediator;
            _dbContext = dbContext;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestModel model)
        {
            var host = HttpContext.Request.Host.Host;
            var tenant = _dbContext.Tenants.FirstOrDefault(t => t.Domain == host);

            if (tenant == null)
            {
                tenant = new IdentityService.Entities.Tenant
                {
                    Id = System.Guid.NewGuid(),
                    Name = "Local Development",
                    Domain = host,
                    ConnectionString = _config.GetConnectionString("IdentityDB") ?? string.Empty
                };

                _dbContext.Tenants.Add(tenant);
                await _dbContext.SaveChangesAsync();
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                TenantId = tenant.Id
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if(!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }

            return Ok(new
            {
                message = "User registered successfully"
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestModel model)
        {
            var host = HttpContext.Request.Host.Host;
            string token = await _mediator.Send(new LoginUserCommand(model.Email, model.Password, host));
            return Ok(new {token});
        }

        private string GenerateJwtToken(ApplicationUser user) {
            var audience = _config["Jwt:Audiences:0"] ?? "urlshortent_api";

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: audience,
                claims : claims,
                expires: DateTime.Now.AddMinutes(5),
                signingCredentials: creds
            );



            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [HttpGet("login-microsoft")]
        public IActionResult LoginWithMicrosoft()
        {
            var redirectUrl = Url.Action(nameof(HandleMicrosoftResponse), "Auth");
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
        }

        [HttpGet("signin-microsoft")]
        public async Task<IActionResult> HandleMicrosoftResponse()
        {
            var result = await HttpContext.AuthenticateAsync(OpenIdConnectDefaults.AuthenticationScheme);
            if (!result.Succeeded) return Unauthorized();

            var claims = result.Principal.Claims;
            var userId = claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            var userEmail = claims.FirstOrDefault(c => c.Type == "email")?.Value;

            return Ok(new { UserId = userId, Email = userEmail });
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
            return Ok("Logged out successfully!");
        }

        [Authorize]
        [HttpGet("secure-data")]
        public IActionResult GetSecureData()
        {
            return Ok(new { message = "You have accessed a protected resource!" });
        }
    }
}
