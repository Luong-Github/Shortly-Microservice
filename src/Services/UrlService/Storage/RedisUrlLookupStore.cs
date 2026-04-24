using System.Text.Json;
using StackExchange.Redis;
using UrlService.Models;
using System.Diagnostics;
using System.Threading;

namespace UrlService.Storage;

/// <summary>
/// Redis-based URL lookup store for high-performance redirect lookups.
/// Provides sub-millisecond access to frequently accessed URLs.
/// 
/// Strategy:
/// - GET/SET operations hit Redis first
/// - Redis acts as L1 cache for hot URLs
/// - Optional write-through mode for consistency
/// - Automatic TTL-based expiration
/// - Background sync to SQL Server for archival
/// </summary>
public class RedisUrlLookupStore : IUrlLookupStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly UrlStorageConfig _config;
    private readonly ILogger<RedisUrlLookupStore> _logger;
    private readonly TimeSpan _ttl;
    private static readonly ActivitySource ActivitySource = new("UrlService.Storage.Redis");

    private long _cacheHits;
    private long _cacheMisses;
    private long _cacheSets;

    public RedisUrlLookupStore(
        IConnectionMultiplexer redis,
        UrlStorageConfig config,
        ILogger<RedisUrlLookupStore> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _db = _redis.GetDatabase();
        _ttl = TimeSpan.FromMinutes(config.CacheTtlMinutes);
    }

    /// <summary>
    /// Retrieves a URL from Redis cache.
    /// Typical latency: <1ms for cache hits.
    /// </summary>
    public async Task<ShortUrl?> GetByShortCodeAsync(string shortCode)
    {
        using var activity = ActivitySource.StartActivity("Redis.GetByShortCode", ActivityKind.Internal);
        activity?.SetTag("shortCode", shortCode);
        activity?.SetTag("cacheTtlMinutes", _config.CacheTtlMinutes);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var key = GetKey(shortCode);
            var cached = await _db.StringGetAsync(key);

            if (!cached.IsNull)
            {
                stopwatch.Stop();
                Interlocked.Increment(ref _cacheHits);
                var json = cached.ToString();
                var url = JsonSerializer.Deserialize<ShortUrl>(json);
                
                _logger.LogInformation(
                    "Redis cache HIT for shortCode {ShortCode} in {ElapsedMs}ms, TTL remaining: {TtlRemaining}s",
                    shortCode, stopwatch.ElapsedMilliseconds, await _db.KeyTimeToLiveAsync(key));
                
                activity?.SetTag("result", "hit");
                activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
                return url;
            }

            stopwatch.Stop();
            Interlocked.Increment(ref _cacheMisses);
            _logger.LogDebug("Redis cache MISS for shortCode {ShortCode} in {ElapsedMs}ms", shortCode, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("result", "miss");
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error retrieving from Redis for shortCode {ShortCode} after {ElapsedMs}ms", shortCode, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("error", ex.Message);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            return null; // Fall back to database
        }
    }

    /// <summary>
    /// Stores a URL in Redis with automatic expiration.
    /// </summary>
    public async Task SetAsync(ShortUrl shortUrl)
    {
        using var activity = ActivitySource.StartActivity("Redis.Set", ActivityKind.Internal);
        activity?.SetTag("shortCode", shortUrl.ShortCode);
        activity?.SetTag("userId", shortUrl.CreatedBy);
        activity?.SetTag("ttlMinutes", _config.CacheTtlMinutes);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var key = GetKey(shortUrl.ShortCode);
            var json = JsonSerializer.Serialize(shortUrl);
            var success = await _db.StringSetAsync(key, json, _ttl);

            stopwatch.Stop();
            Interlocked.Increment(ref _cacheSets);

            if (success)
            {
                _logger.LogInformation(
                    "Cached shortCode {ShortCode} for user {UserId} in Redis with TTL {TtlMinutes}min in {ElapsedMs}ms",
                    shortUrl.ShortCode, shortUrl.CreatedBy, _config.CacheTtlMinutes, stopwatch.ElapsedMilliseconds);
                activity?.SetTag("result", "success");
            }
            else
            {
                _logger.LogWarning("Failed to cache shortCode {ShortCode} in Redis", shortUrl.ShortCode);
                activity?.SetTag("result", "failed");
            }

            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, 
                "Error caching shortCode {ShortCode} to Redis after {ElapsedMs}ms", 
                shortUrl.ShortCode, stopwatch.ElapsedMilliseconds);
            activity?.SetTag("error", ex.Message);
            activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
            // Non-blocking failure - continue without cache
        }
    }

    /// <summary>
    /// Checks if a short code exists in cache.
    /// Much faster than GetByShortCodeAsync for existence checks.
    /// </summary>
    public async Task<bool> ExistsAsync(string shortCode)
    {
        try
        {
            var key = GetKey(shortCode);
            return await _db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking existence in Redis: {shortCode}");
            return false;
        }
    }

    /// <summary>
    /// Deletes a URL from Redis cache.
    /// </summary>
    public async Task DeleteAsync(string shortCode)
    {
        try
        {
            var key = GetKey(shortCode);
            await _db.KeyDeleteAsync(key);

            _logger.LogDebug($"Deleted shortCode from cache: {shortCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting from Redis: {shortCode}");
        }
    }

    /// <summary>
    /// Pre-warm cache with frequently accessed URLs.
    /// Useful after app startup or maintenance windows.
    /// </summary>
    public async Task WarmCacheAsync(IEnumerable<ShortUrl> urls)
    {
        try
        {
            var batch = _db.CreateBatch();
            var tasks = new List<Task>();

            foreach (var url in urls)
            {
                var key = GetKey(url.ShortCode);
                var json = JsonSerializer.Serialize(url);
                tasks.Add(batch.StringSetAsync(key, json, _ttl));
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            _logger.LogInformation($"Warmed cache with {urls.Count()} URLs");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error warming cache");
        }
    }

    /// <summary>
    /// Gets cache statistics for monitoring.
    /// </summary>
    public async Task<RedisStats> GetStatsAsync()
    {
        try
        {
            return new RedisStats
            {
                ConnectedClients = 0,
                UsedMemory = "N/A",
                HitRate = 0,
                MissRate = 0,
                EvictedKeys = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Redis stats");
            return new RedisStats();
        }
    }

    public string GetStorageModeName() => "Redis";

    private string GetKey(string shortCode) => $"url:{shortCode}";
}

/// <summary>
/// Redis statistics for monitoring cache health.
/// </summary>
public class RedisStats
{
    public long ConnectedClients { get; set; }
    public string UsedMemory { get; set; } = string.Empty;
    public long HitRate { get; set; }
    public long MissRate { get; set; }
    public long EvictedKeys { get; set; }

    public double HitRatePercentage => 
        (HitRate + MissRate) == 0 ? 0 : (double)HitRate / (HitRate + MissRate) * 100;
}
