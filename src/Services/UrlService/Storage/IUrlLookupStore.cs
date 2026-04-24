using UrlService.Models;

namespace UrlService.Storage;

/// <summary>
/// Defines the storage strategy for URL lookups.
/// </summary>
public enum UrlStorageMode
{
    /// <summary>
    /// SQL Server only (current implementation).
    /// Best for: Small-scale deployments, development.
    /// </summary>
    SqlServer,

    /// <summary>
    /// Redis for hot lookups + SQL Server for archival.
    /// Best for: High-traffic scenarios, production with millions of redirects.
    /// </summary>
    RedisCached,

    /// <summary>
    /// AWS DynamoDB for primary storage + Redis for hot cache.
    /// Best for: AWS-native deployments, serverless scaling, cost optimization.
    /// </summary>
    DynamoDb
}

/// <summary>
/// Abstraction layer for URL storage backends.
/// Enables switching between SQL Server, Redis, and DynamoDB without code changes.
/// </summary>
public interface IUrlLookupStore
{
    /// <summary>
    /// Retrieves a URL by its short code.
    /// </summary>
    Task<ShortUrl?> GetByShortCodeAsync(string shortCode);

    /// <summary>
    /// Stores a URL with expiration support.
    /// </summary>
    Task SetAsync(ShortUrl shortUrl);

    /// <summary>
    /// Checks if a short code exists without retrieving full data.
    /// Used to avoid expensive lookups in validation.
    /// </summary>
    Task<bool> ExistsAsync(string shortCode);

    /// <summary>
    /// Deletes a URL from the store.
    /// </summary>
    Task DeleteAsync(string shortCode);

    /// <summary>
    /// Gets the storage mode name for logging.
    /// </summary>
    string GetStorageModeName();
}

/// <summary>
/// Configuration for URL storage backends.
/// </summary>
public class UrlStorageConfig
{
    /// <summary>
    /// Primary storage mode.
    /// </summary>
    public UrlStorageMode Mode { get; set; } = UrlStorageMode.SqlServer;

    /// <summary>
    /// Redis cache TTL in minutes (used in RedisCached mode).
    /// Default: 7 days.
    /// </summary>
    public int CacheTtlMinutes { get; set; } = 7 * 24 * 60;

    /// <summary>
    /// Enable Redis write-through caching (all writes go to Redis first).
    /// Improves write throughput at cost of eventual consistency.
    /// </summary>
    public bool EnableRedisWriteThrough { get; set; } = false;

    /// <summary>
    /// DynamoDB table name.
    /// Only used in DynamoDb mode.
    /// </summary>
    public string DynamoDbTableName { get; set; } = "ShortUrls";

    /// <summary>
    /// Enable batch operations for better throughput.
    /// </summary>
    public bool EnableBatchOperations { get; set; } = true;

    /// <summary>
    /// Connection string for SQL Server (fallback/archival storage).
    /// </summary>
    public string? SqlConnectionString { get; set; }

    /// <summary>
    /// Redis connection string.
    /// Format: "host:port" or "host:port,password=xxx"
    /// </summary>
    public string? RedisConnectionString { get; set; }
}
