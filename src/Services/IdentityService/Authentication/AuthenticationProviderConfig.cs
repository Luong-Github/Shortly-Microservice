namespace IdentityService.Authentication;

/// <summary>
/// Configuration for different authentication providers.
/// Allows switching between IdentityServer, Cognito, or hybrid approaches.
/// </summary>
public enum AuthenticationMode
{
    /// <summary>
    /// Use local IdentityServer as the primary OAuth2/OIDC provider.
    /// Best for: Complete control, self-hosted scenarios, development/testing.
    /// </summary>
    IdentityServer,

    /// <summary>
    /// Use AWS Cognito as the primary identity provider.
    /// Best for: AWS-managed identity, enterprise SSO, reduced operational overhead.
    /// </summary>
    Cognito,

    /// <summary>
    /// Use IdentityServer with optional Cognito for external SSO/federation.
    /// Best for: Hybrid approach with both internal and external auth methods.
    /// </summary>
    Hybrid
}

/// <summary>
/// Base configuration for authentication providers.
/// </summary>
public class AuthenticationProviderConfig
{
    /// <summary>
    /// Gets the active authentication mode from configuration.
    /// Defaults to IdentityServer for backward compatibility.
    /// </summary>
    public static AuthenticationMode GetActiveMode(IConfiguration configuration)
    {
        var mode = configuration["Authentication:Mode"] ?? "IdentityServer";
        return Enum.TryParse<AuthenticationMode>(mode, out var result) ? result : AuthenticationMode.IdentityServer;
    }

    /// <summary>
    /// JWT validation configuration shared across all modes.
    /// </summary>
    public class JwtValidationConfig
    {
        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string[] Audiences { get; set; } = Array.Empty<string>();
    }
}
