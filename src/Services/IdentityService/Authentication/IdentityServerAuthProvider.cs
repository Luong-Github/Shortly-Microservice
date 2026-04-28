using IdentityService.Configs;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Authentication;

/// <summary>
/// Encapsulates IdentityServer4 configuration and setup.
/// Responsible for OAuth2/OIDC server functionality and user identity management.
/// 
/// Use this provider when:
/// - You need complete control over OAuth2/OIDC flows
/// - Running in development/testing environments
/// - Deploying in non-AWS environments
/// </summary>
public class IdentityServerAuthProvider
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;

    public IdentityServerAuthProvider(IServiceCollection services, IConfiguration configuration)
    {
        _services = services;
        _configuration = configuration;
    }

    /// <summary>
    /// Configures ASP.NET Identity with EntityFramework stores.
    /// Handles user management, password hashing, and user claims.
    /// </summary>
    public IdentityServerAuthProvider AddAspNetIdentity<TUser, TRole>()
        where TUser : class
        where TRole : class
    {
        _services.AddIdentity<TUser, TRole>()
            .AddEntityFrameworkStores<IdentityService.Data.AppIdentityDbContext>()
            .AddDefaultTokenProviders();

        return this;
    }

    /// <summary>
    /// Configures IdentityServer as an OAuth2/OIDC authorization server.
    /// Handles token issuance, user authentication, and API protection.
    /// </summary>
    public IdentityServerAuthProvider AddIdentityServerProvider()
    {
        _services.AddIdentityServer(options =>
        {
            // Configure IdentityServer behavior
            options.Authentication.CookieLifetime = TimeSpan.FromHours(2);

            // Raise events for auditing
            options.Events.RaiseErrorEvents = true;
            options.Events.RaiseInformationEvents = true;
            options.Events.RaiseFailureEvents = true;
            options.Events.RaiseSuccessEvents = true;

            // TODO: In production, use a proper key store (e.g., AWS Secrets Manager)
            // Current limitation: KeyManagement must be disabled in development
            options.KeyManagement.Enabled = false;
        })
        .AddInMemoryIdentityResources(IdentityServerConfig.GetResources())
        .AddInMemoryApiScopes(IdentityServerConfig.ApiScopes)
        .AddInMemoryApiResources(IdentityServerConfig.GetApis())
        .AddInMemoryClients(IdentityServerConfig.Clients)
        .AddAspNetIdentity<ApplicationUser>()
        .AddDeveloperSigningCredential(persistKey: false); // Avoid writing tempkey.jwk in container

        return this;
    }

    /// <summary>
    /// Adds JWT Bearer authentication for service-to-service communication.
    /// Validates tokens issued by this or other trusted issuers.
    /// </summary>
    public IdentityServerAuthProvider AddJwtBearerValidation(string jwtKey, string jwtIssuer, string[] audiences)
    {
        _services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
           .AddJwtBearer(options =>
           {
               options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
               {
                   ValidateIssuer = true,
                   ValidateAudience = true,
                   ValidateLifetime = true,
                   ValidateIssuerSigningKey = true,
                   ValidIssuer = jwtIssuer,
                   ValidAudiences = audiences.Where(a => !string.IsNullOrEmpty(a)).ToArray(),
                   IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                       System.Text.Encoding.UTF8.GetBytes(jwtKey))
               };
           });

        return this;
    }

    /// <summary>
    /// Adds authorization policies for role-based access control.
    /// </summary>
    public IdentityServerAuthProvider AddAuthorizationPolicies()
    {
        _services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
            options.AddPolicy("UserPolicy", policy => policy.RequireRole("User"));
        });

        return this;
    }

    /// <summary>
    /// Gets the middleware to be added to the pipeline.
    /// Must be called in the same order as configuration.
    /// </summary>
    public static void UseIdentityServerMiddleware(WebApplication app)
    {
        // IdentityServer must come before authorization
        app.UseIdentityServer();
        app.UseAuthorization();
    }
}
