using Microsoft.EntityFrameworkCore;
using UrlService.Data;
using UrlService.Models;

namespace UrlService.Storage;

/// <summary>
/// SQL Server-based URL lookup store.
/// Provides reliable, transactional storage for URL mappings.
/// 
/// Best for:
/// - Development and testing
/// - Compliance requirements (full audit trail)
/// - Small-scale deployments (<10k redirects/day)
/// - Long-term archival storage
/// </summary>
public class SqlServerUrlLookupStore : IUrlLookupStore
{
    private readonly IDbContextFactory<UrlDbContext> _contextFactory;
    private readonly ILogger<SqlServerUrlLookupStore> _logger;

    public SqlServerUrlLookupStore(
        IDbContextFactory<UrlDbContext> contextFactory,
        ILogger<SqlServerUrlLookupStore> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves a URL from SQL Server by short code.
    /// Typical latency: 5-50ms depending on database load and network.
    /// 
    /// Optimization:
    /// - Uses unique index on ShortCode for fast lookup
    /// - Includes expiration check
    /// </summary>
    public async Task<ShortUrl?> GetByShortCodeAsync(string shortCode)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var url = await context.ShortUrls
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.ShortCode == shortCode && !u.IsDeleted);

            if (url == null)
            {
                _logger.LogDebug($"Short code not found: {shortCode}");
                return null;
            }

            // Check if expired
            if (IsExpired(url))
            {
                _logger.LogInformation($"Short code expired: {shortCode}");
                _ = Task.Run(() => DeleteAsync(shortCode)); // Fire-and-forget cleanup
                return null;
            }

            _logger.LogDebug($"Retrieved short code: {shortCode}");
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving from database: {shortCode}");
            throw;
        }
    }

    /// <summary>
    /// Retrieves all URLs created by a specific user.
    /// Used for user's URL listing/management pages.
    /// </summary>
    public async Task<List<ShortUrl>> GetAllByUserIdAsync(Guid userId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var urls = await context.ShortUrls
                .AsNoTracking()
                .Where(u => u.CreatedBy == userId && !u.IsDeleted)
                .OrderByDescending(u => u.CreatedDate)
                .ToListAsync();

            // Filter out expired URLs
            var validUrls = urls.Where(u => !IsExpired(u)).ToList();

            _logger.LogDebug($"Retrieved {validUrls.Count} URLs for user {userId}");
            return validUrls;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving URLs for user: {userId}");
            throw;
        }
    }

    /// <summary>
    /// Stores a URL in SQL Server.
    /// </summary>
    public async Task SetAsync(ShortUrl shortUrl)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            context.ShortUrls.Add(shortUrl);
            await context.SaveChangesAsync();

            _logger.LogInformation($"Stored short code: {shortUrl.ShortCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error storing to database: {shortUrl.ShortCode}");
            throw;
        }
    }

    /// <summary>
    /// Checks if a short code exists in the database.
    /// More efficient than GetByShortCodeAsync for mere existence checks.
    /// </summary>
    public async Task<bool> ExistsAsync(string shortCode)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ShortUrls
                .AsNoTracking()
                .AnyAsync(u => u.ShortCode == shortCode && !u.IsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking existence in database: {shortCode}");
            throw;
        }
    }

    /// <summary>
    /// Soft-deletes a URL by marking as deleted.
    /// Preserves history for auditing.
    /// </summary>
    public async Task DeleteAsync(string shortCode)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var url = await context.ShortUrls
                .FirstOrDefaultAsync(u => u.ShortCode == shortCode);

            if (url != null)
            {
                url.IsDeleted = true;
                await context.SaveChangesAsync();

                _logger.LogInformation($"Deleted short code: {shortCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting from database: {shortCode}");
            throw;
        }
    }

    /// <summary>
    /// Bulk upsert for batch operations.
    /// More efficient than individual inserts.
    /// </summary>
    public async Task BulkUpsertAsync(IEnumerable<ShortUrl> urls)
    {
        try
        {
            var batch = urls.ToList();
            await using var context = await _contextFactory.CreateDbContextAsync();
            
            foreach (var url in batch)
            {
                var existing = await context.ShortUrls
                    .FirstOrDefaultAsync(u => u.ShortCode == url.ShortCode);

                if (existing != null)
                {
                    context.Entry(existing).CurrentValues.SetValues(url);
                }
                else
                {
                    context.ShortUrls.Add(url);
                }
            }

            await context.SaveChangesAsync();

            _logger.LogInformation($"Bulk upserted {batch.Count} URLs");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk upserting");
            throw;
        }
    }

    /// <summary>
    /// Cleans up expired URLs.
    /// Typically run as a scheduled job.
    /// </summary>
    public async Task<int> CleanupExpiredAsync()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var expiredCount = await context.ShortUrls
                .Where(u => u.ExpirationDate < DateTimeOffset.UtcNow && !u.IsDeleted)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsDeleted, true));

            _logger.LogInformation($"Cleaned up {expiredCount} expired URLs");
            return expiredCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired URLs");
            return 0;
        }
    }

    public string GetStorageModeName() => "SqlServer";

    private static bool IsExpired(ShortUrl url)
    {
        return url.ExpirationDate != default && 
               url.ExpirationDate < DateTimeOffset.UtcNow;
    }
}
