using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Authentication;

/// <summary>
/// Encapsulates AWS Cognito configuration and setup.
/// Responsible for integrating with AWS Cognito as the identity provider.
/// 
/// Use this provider when:
/// - Deploying in AWS environments
/// - Requiring managed identity services
/// - Integrating with enterprise AWS infrastructure
/// - Reducing operational overhead for user management
/// </summary>
public class CognitoAuthProvider
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    public CognitoAuthProvider(IServiceCollection services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    /// <summary>
    /// Adds AWS Cognito integration for SSO and federated authentication.
    /// This allows external identity federation while maintaining service-to-service JWT validation.
    /// </summary>
    public CognitoAuthProvider AddCognitoIdentity()
    {
        _services.AddCognitoIdentity();
        return this;
    }

    /// <summary>
    /// Configures JWT Bearer authentication for Cognito-issued tokens.
    /// Uses Cognito's public keys for token validation (via authority URL).
    /// 
    /// For service-to-service communication, use AddSecureServiceAuthentication instead.
    /// </summary>
    public CognitoAuthProvider AddCognitoJwtValidation()
    {
        var authority = _configuration["Cognito:Authority"] 
            ?? throw new InvalidOperationException("Cognito:Authority must be configured");
        
        var audience = _configuration["Cognito:Audience"] 
            ?? throw new InvalidOperationException("Cognito:Audience must be configured");

        _services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RoleClaimType = "cognito:groups" // Extract Cognito Groups as roles
                };
            });

        return this;
    }

    /// <summary>
    /// Adds a secondary JWT Bearer handler for service-to-service authentication.
    /// This validates internal service tokens separately from Cognito tokens.
    /// 
    /// Allows both:
    /// - User tokens from Cognito (primary flow)
    /// - Service tokens from your identity service (service-to-service)
    /// </summary>
    public CognitoAuthProvider AddSecureServiceAuthentication(string jwtKey, string jwtIssuer, string[] audiences)
    {
        // Note: Adding a second authentication scheme requires manual scheme selection
        // See the middleware in the main auth pipeline
        
        _services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer("ServiceJwt", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudiences = audiences.Where(a => !string.IsNullOrEmpty(a)).ToArray(),
                    IssuerSigningKey = new SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(jwtKey))
                };
            });

        return this;
    }

    /// <summary>
    /// Adds authorization policies for Cognito-authenticated users.
    /// Maps Cognito groups to authorization policies.
    /// </summary>
    public CognitoAuthProvider AddCognitoAuthorizationPolicies()
    {
        _services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminPolicy", policy => 
                policy.RequireRole("Admin") // Cognito group name
            );
            options.AddPolicy("UserPolicy", policy => 
                policy.RequireRole("User") // Cognito group name
            );
        });

        return this;
    }

    /// <summary>
    /// Gets the middleware to be added to the pipeline for Cognito.
    /// </summary>
    public static void UseCognitoMiddleware(WebApplication app)
    {
        // For Cognito, we only need standard authentication/authorization
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
