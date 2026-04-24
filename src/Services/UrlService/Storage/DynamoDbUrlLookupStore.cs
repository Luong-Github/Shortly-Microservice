using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text.Json;
using UrlService.Models;

namespace UrlService.Storage;

/// <summary>
/// AWS DynamoDB-based URL lookup store.
/// Provides serverless, auto-scaling URL storage optimized for read-heavy workloads.
/// 
/// Best for:
/// - AWS deployments
/// - Auto-scaling without capacity planning
/// - Cost optimization (pay per request)
/// - Global distribution (DynamoDB Global Tables)
/// - Millions of redirects per second
/// 
/// Schema:
/// - Partition Key: ShortCode (string)
/// - Sort Key: none (optional: CreatedDate for time-series queries)
/// - TTL: ExpirationDate (automatic cleanup)
/// - GSI: CreatedBy + CreatedDate (for user's URL listing)
/// </summary>
public class DynamoDbUrlLookupStore : IUrlLookupStore
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly UrlStorageConfig _config;
    private readonly ILogger<DynamoDbUrlLookupStore> _logger;

    public DynamoDbUrlLookupStore(
        IAmazonDynamoDB dynamoDb,
        UrlStorageConfig config,
        ILogger<DynamoDbUrlLookupStore> logger)
    {
        _dynamoDb = dynamoDb ?? throw new ArgumentNullException(nameof(dynamoDb));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves a URL from DynamoDB by short code.
    /// Benefits of DynamoDB:
    /// - Consistent sub-100ms latency
    /// - Auto-scaling (no capacity planning)
    /// - Global distribution via Global Tables
    /// - Built-in TTL-based expiration
    /// </summary>
    public async Task<ShortUrl?> GetByShortCodeAsync(string shortCode)
    {
        try
        {
            var request = new GetItemRequest
            {
                TableName = _config.DynamoDbTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { "ShortCode", new AttributeValue { S = shortCode } }
                },
                ConsistentRead = false // Eventually consistent for reads
            };

            var response = await _dynamoDb.GetItemAsync(request);

            if (!response.Item.Any())
            {
                _logger.LogDebug($"Short code not found in DynamoDB: {shortCode}");
                return null;
            }

            var url = ConvertFromDynamoDb(response.Item);

            // Check expiration
            if (url.ExpirationDate != default && url.ExpirationDate < DateTimeOffset.UtcNow)
            {
                _logger.LogInformation($"Short code expired: {shortCode}");
                _ = Task.Run(() => DeleteAsync(shortCode)); // Fire-and-forget cleanup
                return null;
            }

            _logger.LogDebug($"Retrieved short code from DynamoDB: {shortCode}");
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving from DynamoDB: {shortCode}");
            throw;
        }
    }

    /// <summary>
    /// Stores a URL in DynamoDB with TTL support.
    /// </summary>
    public async Task SetAsync(ShortUrl shortUrl)
    {
        try
        {
            var item = ConvertToDynamoDb(shortUrl);

            var request = new PutItemRequest
            {
                TableName = _config.DynamoDbTableName,
                Item = item
            };

            await _dynamoDb.PutItemAsync(request);

            _logger.LogInformation($"Stored short code in DynamoDB: {shortUrl.ShortCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error storing to DynamoDB: {shortUrl.ShortCode}");
            throw;
        }
    }

    /// <summary>
    /// Checks existence in DynamoDB using Query with projection.
    /// Faster than full item retrieval.
    /// </summary>
    public async Task<bool> ExistsAsync(string shortCode)
    {
        try
        {
            var request = new GetItemRequest
            {
                TableName = _config.DynamoDbTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { "ShortCode", new AttributeValue { S = shortCode } }
                },
                ProjectionExpression = "ShortCode" // Only retrieve the key
            };

            var response = await _dynamoDb.GetItemAsync(request);
            return response.Item.Any();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking existence in DynamoDB: {shortCode}");
            throw;
        }
    }

    /// <summary>
    /// Marks a URL as deleted in DynamoDB.
    /// Note: Actual deletion happens via TTL.
    /// </summary>
    public async Task DeleteAsync(string shortCode)
    {
        try
        {
            var request = new UpdateItemRequest
            {
                TableName = _config.DynamoDbTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { "ShortCode", new AttributeValue { S = shortCode } }
                },
                UpdateExpression = "SET IsDeleted = :val",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":val", new AttributeValue { BOOL = true } }
                }
            };

            await _dynamoDb.UpdateItemAsync(request);

            _logger.LogInformation($"Deleted short code in DynamoDB: {shortCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting from DynamoDB: {shortCode}");
            throw;
        }
    }

    /// <summary>
    /// Batch get operation for efficient bulk retrieval.
    /// </summary>
    public async Task<List<ShortUrl>> BatchGetAsync(IEnumerable<string> shortCodes)
    {
        try
        {
            var codes = shortCodes.ToList();
            var request = new BatchGetItemRequest
            {
                RequestItems = new Dictionary<string, KeysAndAttributes>
                {
                    {
                        _config.DynamoDbTableName,
                        new KeysAndAttributes
                        {
                            Keys = codes
                                .Select(code => new Dictionary<string, AttributeValue>
                                {
                                    { "ShortCode", new AttributeValue { S = code } }
                                })
                                .ToList()
                        }
                    }
                }
            };

            var response = await _dynamoDb.BatchGetItemAsync(request);
            var items = response.Responses[_config.DynamoDbTableName];

            return items
                .Select(ConvertFromDynamoDb)
                .Where(url => !IsExpired(url))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch getting from DynamoDB");
            throw;
        }
    }

    /// <summary>
    /// Batch write operation for efficient bulk insertion.
    /// </summary>
    public async Task BatchSetAsync(IEnumerable<ShortUrl> urls)
    {
        try
        {
            var batch = urls.ToList();
            var request = new BatchWriteItemRequest
            {
                RequestItems = new Dictionary<string, List<WriteRequest>>
                {
                    {
                        _config.DynamoDbTableName,
                        batch
                            .Select(url => new WriteRequest
                            {
                                PutRequest = new PutRequest
                                {
                                    Item = ConvertToDynamoDb(url)
                                }
                            })
                            .ToList()
                    }
                }
            };

            await _dynamoDb.BatchWriteItemAsync(request);

            _logger.LogInformation($"Batch stored {batch.Count} URLs in DynamoDB");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch writing to DynamoDB");
            throw;
        }
    }

    public string GetStorageModeName() => "DynamoBb";

    private Dictionary<string, AttributeValue> ConvertToDynamoDb(ShortUrl url)
    {
        return new Dictionary<string, AttributeValue>
        {
            { "ShortCode", new AttributeValue { S = url.ShortCode } },
            { "OriginalUrl", new AttributeValue { S = url.OriginalUrl } },
            { "UserId", new AttributeValue { S = url.CreatedBy.ToString() } },
            { "CreatedDate", new AttributeValue { N = url.CreatedDate.ToUnixTimeSeconds().ToString() } },
            { "ExpirationDate", new AttributeValue { N = url.ExpirationDate.ToUnixTimeSeconds().ToString() } },
            { "IsDeleted", new AttributeValue { BOOL = url.IsDeleted } },
            { "Data", new AttributeValue { S = JsonSerializer.Serialize(url) } }
        };
    }

    private ShortUrl ConvertFromDynamoDb(Dictionary<string, AttributeValue> item)
    {
        if (item.TryGetValue("Data", out var dataAttr) && !string.IsNullOrEmpty(dataAttr.S))
        {
            return JsonSerializer.Deserialize<ShortUrl>(dataAttr.S) 
                ?? throw new InvalidOperationException("Failed to deserialize URL");
        }

        return new ShortUrl
        {
            ShortCode = item["ShortCode"].S,
            OriginalUrl = item["OriginalUrl"].S,
            CreatedBy = Guid.Parse(item["UserId"].S),
            CreatedDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(item["CreatedDate"].N)),
            ExpirationDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(item["ExpirationDate"].N)),
            IsDeleted = item["IsDeleted"].BOOL
        };
    }

    private static bool IsExpired(ShortUrl url)
    {
        return url.ExpirationDate != default && 
               url.ExpirationDate < DateTimeOffset.UtcNow;
    }
}
