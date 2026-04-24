using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace IdentityService.Authentication;

/// <summary>
/// Handles JWT token validation for service-to-service communication.
/// Separate from user authentication to maintain clear separation of concerns.
/// 
/// Responsibilities:
/// - Validate JWTs issued by the Identity Service for internal APIs
/// - Support multiple audiences (different services with different token requirements)
/// - Handle token scheme negotiation
/// </summary>
public class JwtValidationProvider
{
    private readonly IServiceCollection _services;

    public JwtValidationProvider(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configures JWT Bearer authentication for service-to-service token validation.
    /// Call this when you need to validate JWTs WITHOUT an OAuth2/OIDC server (e.g., Cognito primary auth mode).
    /// </summary>
    public JwtValidationProvider AddJwtBearerAuthentication(string jwtKey, string jwtIssuer, string[] audiences)
    {
        _services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false; // Set to true in production
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudiences = audiences.Where(a => !string.IsNullOrEmpty(a)).ToArray(),
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                };
            });

        return this;
    }

    /// <summary>
    /// Adds a named JWT Bearer scheme for scenarios where multiple authentication schemes need to coexist.
    /// Example: User tokens from Cognito + Service tokens from local Identity Server
    /// </summary>
    public JwtValidationProvider AddJwtBearerScheme(string schemeName, string jwtKey, string jwtIssuer, string[] audiences)
    {
        _services.AddAuthentication()
            .AddJwtBearer(schemeName, options =>
            {
                options.RequireHttpsMetadata = false;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudiences = audiences.Where(a => !string.IsNullOrEmpty(a)).ToArray(),
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                };
            });

        return this;
    }

    /// <summary>
    /// Adds authorization policies for JWT-validated requests.
    /// These policies work with any authentication scheme (IdentityServer, Cognito, JWT).
    /// </summary>
    public JwtValidationProvider AddAuthorizationPolicies()
    {
        _services.AddAuthorization(options =>
        {
            // Role-based policies
            options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
            options.AddPolicy("UserPolicy", policy => policy.RequireRole("User"));

            // Scope-based policies (for API scopes)
            options.AddPolicy("FullAccessPolicy", policy => 
                policy.RequireClaim("scope", "api.full")
            );
            options.AddPolicy("ReadOnlyPolicy", policy => 
                policy.RequireClaim("scope", "api.read")
            );
        });

        return this;
    }

    /// <summary>
    /// Gets middleware used for JWT validation.
    /// Called in the app pipeline after configuration.
    /// </summary>
    public static void UseJwtValidationMiddleware(WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
