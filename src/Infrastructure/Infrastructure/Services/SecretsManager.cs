using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Infrastructure.Services;

/// <summary>
/// Manages loading secrets from AWS Secrets Manager with fallback to configuration.
/// For local development, use appsettings.Development.json or User Secrets.
/// For production, secrets are loaded from AWS Secrets Manager.
/// </summary>
public class SecretsManager
{
    private readonly IConfiguration _configuration;
    private readonly IAmazonSecretsManager? _secretsManagerClient;
    private static readonly Dictionary<string, string> _cachedSecrets = new();

    public SecretsManager(IConfiguration configuration)
    {
        _configuration = configuration;
        
        // Only initialize AWS Secrets Manager if AWS credentials are available
        try
        {
            _secretsManagerClient = new AmazonSecretsManagerClient();
        }
        catch
        {
            _secretsManagerClient = null;
        }
    }

    /// <summary>
    /// Gets a secret value from AWS Secrets Manager or configuration.
    /// Priority: AWS Secrets Manager (production) > Configuration (development)
    /// </summary>
    public async Task<string?> GetSecretAsync(string secretName, string? defaultValue = null)
    {
        // Check cache first
        if (_cachedSecrets.TryGetValue(secretName, out var cachedValue))
        {
            return cachedValue;
        }

        // Try AWS Secrets Manager first (production)
        if (_secretsManagerClient != null)
        {
            try
            {
                var response = await _secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest
                {
                    SecretId = secretName
                });

                var secretValue = response.SecretString;
                if (secretValue != null)
                {
                    _cachedSecrets[secretName] = secretValue;
                    return secretValue;
                }
            }
            catch (ResourceNotFoundException)
            {
                // Secret doesn't exist in AWS Secrets Manager, fall through to configuration
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to retrieve secret '{secretName}' from AWS: {ex.Message}");
            }
        }

        // Fall back to configuration (development)
        var configValue = _configuration[secretName];
        if (configValue != null)
        {
            _cachedSecrets[secretName] = configValue;
            return configValue;
        }

        // Return default if provided
        if (defaultValue != null)
        {
            _cachedSecrets[secretName] = defaultValue;
            return defaultValue;
        }

        return null;
    }

    /// <summary>
    /// Gets a JSON object secret from AWS Secrets Manager.
    /// Typically used for complex configurations like database credentials.
    /// </summary>
    public async Task<T?> GetJsonSecretAsync<T>(string secretName) where T : class
    {
        var secretValue = await GetSecretAsync(secretName);
        if (string.IsNullOrEmpty(secretValue))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(secretValue);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Warning: Failed to parse JSON secret '{secretName}': {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// JWT configuration that can be loaded from secrets
/// </summary>
public class JwtConfiguration
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string[] Audiences { get; set; } = Array.Empty<string>();
    public int ExpirationMinutes { get; set; } = 60;
}
