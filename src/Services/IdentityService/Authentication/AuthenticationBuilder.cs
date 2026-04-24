using Infrastructure.Services;

namespace IdentityService.Authentication;

/// <summary>
/// Fluent builder for configuring authentication providers.
/// Simplifies the program.cs by providing a clean API for setting up auth.
/// 
/// Usage:
///   var authBuilder = new AuthenticationBuilder(services, configuration, secretsManager);
///   authBuilder.ConfigureIdentityServerMode();
///   // or
///   authBuilder.ConfigureCognitoMode();
/// </summary>
public class AuthenticationBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private readonly SecretsManager _secretsManager;

    public AuthenticationBuilder(
        IServiceCollection services, 
        IConfiguration configuration, 
        SecretsManager secretsManager)
    {
        _services = services;
        _configuration = configuration;
        _secretsManager = secretsManager;
    }

    /// <summary>
    /// Configures the application to use IdentityServer as the primary authentication provider.
    /// Includes:
    /// - ASP.NET Identity for user management
    /// - IdentityServer4 for OAuth2/OIDC server
    /// - JWT Bearer validation for service-to-service communication
    /// - Authorization policies for role-based access control
    /// </summary>
    public async Task ConfigureIdentityServerModeAsync()
    {
        // Load JWT secrets
        var jwtKey = await _secretsManager.GetSecretAsync("Jwt:Key");
        var jwtIssuer = await _secretsManager.GetSecretAsync("Jwt:Issuer");
        var jwtAudiences = new[] {
            await _secretsManager.GetSecretAsync("Jwt:Audiences:0") ?? "urlshortent_api",
            await _secretsManager.GetSecretAsync("Jwt:Audiences:1") ?? "analytics_api"
        };

        ValidateJwtConfiguration(jwtKey, jwtIssuer);

        // Apply IdentityServer provider configuration
        var idServerProvider = new IdentityServerAuthProvider(_services, _configuration);
        idServerProvider
            .AddAspNetIdentity<IdentityService.Models.ApplicationUser, Microsoft.AspNetCore.Identity.IdentityRole>()
            .AddIdentityServerProvider()
            .AddJwtBearerValidation(jwtKey, jwtIssuer, jwtAudiences)
            .AddAuthorizationPolicies();
    }

    /// <summary>
    /// Configures the application to use AWS Cognito as the primary authentication provider.
    /// Includes:
    /// - AWS Cognito for user management and SSO
    /// - Optional service-to-service JWT validation
    /// - Authorization policies mapped to Cognito groups
    /// </summary>
    public async Task ConfigureCognitoModeAsync(bool includeServiceJwt = true)
    {
        // Validate Cognito configuration
        var authority = _configuration["Cognito:Authority"];
        var audience = _configuration["Cognito:Audience"];

        if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(audience))
        {
            throw new InvalidOperationException(
                "Cognito mode requires 'Cognito:Authority' and 'Cognito:Audience' configuration");
        }

        var cognitoProvider = new CognitoAuthProvider(_services, _configuration);
        cognitoProvider
            .AddCognitoIdentity()
            .AddCognitoJwtValidation()
            .AddCognitoAuthorizationPolicies();

        // Optionally add service-to-service JWT validation for internal APIs
        if (includeServiceJwt)
        {
            var jwtKey = await _secretsManager.GetSecretAsync("Jwt:Key");
            var jwtIssuer = await _secretsManager.GetSecretAsync("Jwt:Issuer");
            var jwtAudiences = new[] {
                await _secretsManager.GetSecretAsync("Jwt:Audiences:0") ?? "urlshortent_api",
                await _secretsManager.GetSecretAsync("Jwt:Audiences:1") ?? "analytics_api"
            };

            ValidateJwtConfiguration(jwtKey, jwtIssuer);
            cognitoProvider.AddSecureServiceAuthentication(jwtKey, jwtIssuer, jwtAudiences);
        }
    }

    /// <summary>
    /// Configures the application to use a hybrid approach:
    /// - IdentityServer as primary for internal services
    /// - Cognito for external/SSO authentication
    /// 
    /// This allows both flows to coexist:
    /// 1. Users authenticate via Cognito for SSO
    /// 2. Services use IdentityServer-issued JWTs for API access
    /// </summary>
    public async Task ConfigureHybridModeAsync()
    {
        // Load JWT secrets
        var jwtKey = await _secretsManager.GetSecretAsync("Jwt:Key");
        var jwtIssuer = await _secretsManager.GetSecretAsync("Jwt:Issuer");
        var jwtAudiences = new[] {
            await _secretsManager.GetSecretAsync("Jwt:Audiences:0") ?? "urlshortent_api",
            await _secretsManager.GetSecretAsync("Jwt:Audiences:1") ?? "analytics_api"
        };

        ValidateJwtConfiguration(jwtKey, jwtIssuer);

        // Configure IdentityServer for internal service auth
        var idServerProvider = new IdentityServerAuthProvider(_services, _configuration);
        idServerProvider
            .AddAspNetIdentity<IdentityService.Models.ApplicationUser, Microsoft.AspNetCore.Identity.IdentityRole>()
            .AddIdentityServerProvider()
            .AddJwtBearerValidation(jwtKey, jwtIssuer, jwtAudiences)
            .AddAuthorizationPolicies();

        // Add Cognito for external/SSO auth (optional)
        var cognitoEnabled = _configuration.GetValue<bool>("Cognito:Enabled", false);
        if (cognitoEnabled)
        {
            var cognitoProvider = new CognitoAuthProvider(_services, _configuration);
            cognitoProvider
                .AddCognitoIdentity()
                .AddCognitoJwtValidation();
        }
    }

    /// <summary>
    /// Gets the middleware setup for the configured authentication mode.
    /// Must be called after ConfigureXxxModeAsync() and before app.Run().
    /// </summary>
    public void UseConfiguredMiddleware(WebApplication app, AuthenticationMode mode)
    {
        switch (mode)
        {
            case AuthenticationMode.IdentityServer:
                IdentityServerAuthProvider.UseIdentityServerMiddleware(app);
                break;

            case AuthenticationMode.Cognito:
                CognitoAuthProvider.UseCognitoMiddleware(app);
                break;

            case AuthenticationMode.Hybrid:
                // In hybrid mode, IdentityServer takes precedence
                IdentityServerAuthProvider.UseIdentityServerMiddleware(app);
                break;

            default:
                throw new ArgumentException($"Unknown authentication mode: {mode}");
        }
    }

    /// <summary>
    /// Validates that JWT configuration has all required values.
    /// </summary>
    private static void ValidateJwtConfiguration(string? jwtKey, string? jwtIssuer)
    {
        if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer))
        {
            throw new InvalidOperationException(
                "JWT configuration (Jwt:Key, Jwt:Issuer) is missing. " +
                "Configure via AWS Secrets Manager or appsettings.Development.json");
        }

        if (jwtKey.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT Key must be at least 32 characters long for security");
        }
    }
}
