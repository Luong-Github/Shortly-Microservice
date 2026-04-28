using IdentityService.Events;
using IdentityService.Models;
using IdentityService.Services;
using IdentityService.Services.Tenant;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Commands
{
    public class LoginUserCommand : IRequest<string>
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string? Domain { get; set; }
        public LoginUserCommand(string email, string password, string domain)
        {
            Email = email;
            Password = password;
            Domain = domain;
        }
    }

    public class LoginUserCommandHandler : IRequestHandler<LoginUserCommand, string>
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly JwtTokenGenerator _jwtTokenGenerator;
        private readonly IMediator _mediator;

        public LoginUserCommandHandler(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, JwtTokenGenerator jwtTokenGenerator, IMediator mediator)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _jwtTokenGenerator = jwtTokenGenerator;
            _mediator = mediator;
        }

        public async Task<string> Handle(LoginUserCommand request, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(request.Domain) &&
                request.Domain != "localhost" &&
                request.Domain != "127.0.0.1")
            {
                var tenant = await _mediator.Send(new GetTenantAsyncQuery() { Domain = request.Domain });
                if (tenant == null) return null;
            }

            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null || !await _userManager.CheckPasswordAsync(user, request.Password)) 
                throw new UnauthorizedAccessException("Invalid credentials");

            var roles = await _userManager.GetRolesAsync(user);
            var token = _jwtTokenGenerator.GenerateToken(user.Id, user.Email, roles.ToArray());
            await _mediator.Publish(new UserLoggedInEvent(user.Id, user.Email, token));

            return token;
        }
    }
}
