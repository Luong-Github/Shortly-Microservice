using UrlService.Models;
using UrlService.Repositories;
using System.Diagnostics;

namespace UrlService.Storage;

/// <summary>
/// Multi-tier URL repository providing intelligent storage and caching.
/// 
/// Strategies:
/// 1. **RedisCache + SqlServer**: Hot URLs in Redis, cold in SQL
///    - Reads: Redis (fast) → SQL (fallback)
///    - Writes: Redis + SQL (write-through)
///    - TTL: Automatic Redis expiration
///
/// 2. **RedisCache + DynamoDB**: Hot URLs in Redis, archive in DynamoDB
///    - Reads: Redis → DynamoDB (auto-scale)
///    - Writes: Redis → DynamoDB (async)
///    - TTL: Built-in DynamoDB TTL
///
/// 3. **DynamoDB (Primary)**: Single source of truth
///    - Reads: DynamoDB with optional Redis warmup
///    - Writes: DynamoDB
///    - Scaling: Automatic, pay-per-request
/// </summary>
public class CachedUrlRepository : IUrlRepository
{
    private readonly IUrlLookupStore _primaryStore;
    private readonly IUrlLookupStore? _secondaryStore;
    private readonly UrlStorageConfig _config;
    private readonly ILogger<CachedUrlRepository> _logger;
    private static readonly ActivitySource ActivitySource = new("UrlService.Storage");

    public CachedUrlRepository(
        IUrlLookupStore primaryStore,
        IUrlLookupStore? secondaryStore,
        UrlStorageConfig config,
        ILogger<CachedUrlRepository> logger)
    {
        _primaryStore = primaryStore ?? throw new ArgumentNullException(nameof(primaryStore));
        _secondaryStore = secondaryStore;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation(
            "URL Repository initialized: Primary={PrimaryStore}, Secondary={SecondaryStore}, WriteThrough={WriteThrough}",
            primaryStore.GetStorageModeName(),
            secondaryStore?.GetStorageModeName() ?? "none",
            config.EnableRedisWriteThrough);
    }

    /// <summary>
    /// Intelligent retrieval with fallback.
    /// Checks cache first, then database.
    /// </summary>
    public async Task<ShortUrl?> GetByShortCodeAsync(string shortCode)
    {
        using var activity = ActivitySource.StartActivity("GetByShortCode", ActivityKind.Internal);
        activity?.SetTag("shortCode", shortCode);
        activity?.SetTag("primaryStore", _primaryStore.GetStorageModeName());
        activity?.SetTag("secondaryStore", _secondaryStore?.GetStorageModeName() ?? "none");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Try primary store first (typically cache)
            var url = await _primaryStore.GetByShortCodeAsync(shortCode);
            if (url != null)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "Retrieved short code {ShortCode} from primary store {StoreName} in {ElapsedMs}ms",
                    shortCode, _primaryStore.GetStorageModeName(), stopwatch.ElapsedMilliseconds);
                activity?.SetTag("result", "hit");
                activity?.SetTag("store", "primary");
                activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
                return url;
            }

            // Fallback to secondary store if available
            if (_secondaryStore != null)
            {
                url = await _secondaryStore.GetByShortCodeAsync(shortCode);
                if (url != null)
                {
                    // Populate primary cache for future hits
                    _ = Task.Run(() => _primaryStore.SetAsync(url));
                    stopwatch.Stop();
                    _logger.LogInformation(
                        "Retrieved short code {ShortCode} from secondary store {StoreName} in {ElapsedMs}ms (cache populated)",
                        shortCode, _secondaryStore.GetStorageModeName(), stopwatch.ElapsedMilliseconds);
                    activity?.SetTag("result", "miss");
                    activity?.SetTag("store", "secondary");
                    activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
                    return url;
                }
            }

            stopwatch.Stop();
            _logger.LogWarning("Short code {ShortCode} not found in any store after {ElapsedMs}ms", shortCode, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("result", "not_found");
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error retrieving short code {ShortCode} after {ElapsedMs}ms", shortCode, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("error", ex.Message);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Creates a new short URL in all configured stores.
    /// Write-through strategy ensures consistency.
    /// </summary>
    public async Task<ShortUrl> CreateAsync(ShortUrl shortUrl)
    {
        using var activity = ActivitySource.StartActivity("CreateShortUrl", ActivityKind.Internal);
        activity?.SetTag("shortCode", shortUrl.ShortCode);
        activity?.SetTag("userId", shortUrl.CreatedBy);
        activity?.SetTag("originalUrl", shortUrl.OriginalUrl);
        activity?.SetTag("writeThrough", _config.EnableRedisWriteThrough);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Write to primary store
            await _primaryStore.SetAsync(shortUrl);
            stopwatch.Stop();

            _logger.LogInformation(
                "Created short URL {ShortCode} for user {UserId} in primary store {StoreName} in {ElapsedMs}ms",
                shortUrl.ShortCode, shortUrl.CreatedBy, _primaryStore.GetStorageModeName(), stopwatch.ElapsedMilliseconds);

            // Write to secondary store async (don't block on this)
            if (_secondaryStore != null && _config.EnableRedisWriteThrough)
            {
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await _secondaryStore.SetAsync(shortUrl);
                        _logger.LogInformation(
                            "Async write completed for short code {ShortCode} to secondary store {StoreName}",
                            shortUrl.ShortCode, _secondaryStore.GetStorageModeName());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, 
                            "Secondary store write failed for short code {ShortCode} in store {StoreName}",
                            shortUrl.ShortCode, _secondaryStore.GetStorageModeName());
                    }
                });
            }

            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            return shortUrl;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, 
                "Error creating short URL {ShortCode} for user {UserId} after {ElapsedMs}ms",
                shortUrl.ShortCode, shortUrl.CreatedBy, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("error", ex.Message);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Gets all URLs for a user.
    /// Optimized for user listings (from database, not cache).
    /// </summary>
    public async Task<List<ShortUrl>> GetAllByUserId(Guid userId)
    {
        try
        {
            // Query database for user's URLs
            if (_secondaryStore is SqlServerUrlLookupStore sqlStore)
            {
                var urls = await sqlStore.GetAllByUserIdAsync(userId);
                
                // Optionally warm cache with user's recent URLs
                if (urls.Any() && _primaryStore is RedisUrlLookupStore redisStore)
                {
                    _ = Task.Run(() => redisStore.WarmCacheAsync(urls));
                }

                return urls;
            }

            _logger.LogWarning("GetAllByUserId requires SqlServer as secondary store");
            return new List<ShortUrl>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting URLs for user: {userId}");
            throw;
        }
    }

    /// <summary>
    /// Bulk import URLs into all stores.
    /// Optimized for batch operations.
    /// </summary>
    public async Task BulkImportAsync(IEnumerable<ShortUrl> urls)
    {
        var batch = urls.ToList();
        _logger.LogInformation($"Starting bulk import of {batch.Count} URLs");

        try
        {
            var tasks = new List<Task>();

            // Bulk write to primary store
            if (_primaryStore is RedisUrlLookupStore redisStore)
            {
                tasks.Add(redisStore.WarmCacheAsync(batch));
            }
            else if (_primaryStore is DynamoDbUrlLookupStore dynamoStore)
            {
                tasks.Add(dynamoStore.BatchSetAsync(batch));
            }

            // Bulk write to secondary store
            if (_secondaryStore is SqlServerUrlLookupStore sqlStore)
            {
                tasks.Add(sqlStore.BulkUpsertAsync(batch));
            }
            else if (_secondaryStore is DynamoDbUrlLookupStore dynamoDbStore)
            {
                tasks.Add(dynamoDbStore.BatchSetAsync(batch));
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation($"Completed bulk import of {batch.Count} URLs");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error during bulk import");
            throw;
        }
    }

    /// <summary>
    /// Deletes a URL from all stores.
    /// </summary>
    public async Task DeleteAsync(string shortCode)
    {
        try
        {
            var tasks = new[]
            {
                _primaryStore.DeleteAsync(shortCode),
                _secondaryStore?.DeleteAsync(shortCode) ?? Task.CompletedTask
            };

            await Task.WhenAll(tasks.Where(t => t != null));

            _logger.LogInformation($"Deleted short code: {shortCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting short code: {shortCode}");
            throw;
        }
    }
}

/// <summary>
/// Extension methods for IUrlRepository integration.
/// </summary>
public static class RepositoryExtensions
{
    public static bool IsRedis(this IUrlLookupStore store) =>
        store.GetStorageModeName() == "Redis";

    public static bool IsSqlServer(this IUrlLookupStore store) =>
        store.GetStorageModeName() == "SqlServer";

    public static bool IsDynamoDB(this IUrlLookupStore store) =>
        store.GetStorageModeName() == "DynamoDB";
}
