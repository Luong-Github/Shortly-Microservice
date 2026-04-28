using Amazon.DynamoDBv2;
using StackExchange.Redis;
using UrlService.Models;
using UrlService.Repositories;
using UrlService.Data;
using Microsoft.EntityFrameworkCore;

namespace UrlService.Storage;

/// <summary>
/// Factory for creating multi-tier URL repositories.
/// Intelligently combines primary and secondary stores based on configuration.
/// 
/// Topology Combinations:
/// 1. Development: SqlServer only
///    → Fast local development, single store
///
/// 2. Production (High Volume): Redis + SqlServer
///    → Sub-1ms cache hits + reliable persistence
///    → Millions of redirects/day
///
/// 3. Enterprise (Serverless): DynamoDB + Redis
///    → Auto-scaling without infrastructure
///    → Global distribution ready
///
/// 4. Archive (Cost Optimized): DynamoDB only
///    → Single source of truth, auto-scaling
///    → Pay-per-request pricing
/// </summary>
public class UrlStorageFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UrlStorageFactory> _logger;

    public UrlStorageFactory(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<UrlStorageFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates an IUrlRepository with appropriate storage tier combination.
    /// </summary>
    public IUrlRepository CreateRepository()
    {
        var config = GetStorageConfiguration();
        _logger.LogInformation($"Creating URL repository with mode: {config.StorageMode}");

        return config.StorageMode switch
        {
            "Development" => CreateDevelopmentRepository(config),
            "Production" => CreateProductionRepository(config),
            "Enterprise" => CreateEnterpriseRepository(config),
            "Archive" => CreateArchiveRepository(config),
            _ => throw new InvalidOperationException(
                $"Unknown storage mode: {config.StorageMode}. " +
                $"Valid modes: Development, Production, Enterprise, Archive")
        };
    }

    /// <summary>
    /// Development: SqlServer only
    /// - Single store for simplicity
    /// - Fast local iteration
    /// - Easy debugging with direct database access
    /// </summary>
    private IUrlRepository CreateDevelopmentRepository(UrlStorageConfiguration config)
    {
        _logger.LogInformation("Setting up Development repository (SqlServer only)");

        var primaryStore = new SqlServerUrlLookupStore(
            _serviceProvider.GetRequiredService<IDbContextFactory<UrlDbContext>>(),
            _serviceProvider.GetRequiredService<ILogger<SqlServerUrlLookupStore>>());

        return new CachedUrlRepository(
            primaryStore,
            secondaryStore: null,
            config.StorageConfig,
            _serviceProvider.GetRequiredService<ILogger<CachedUrlRepository>>());
    }

    /// <summary>
    /// Production: Redis (L1 cache) + SqlServer (L2 persistent store)
    /// - Sub-millisecond retrieval for millions of redirects/day
    /// - Write-through consistency
    /// - Automatic TTL expiration
    /// - Fallback to database on cache miss
    /// </summary>
    private IUrlRepository CreateProductionRepository(UrlStorageConfiguration config)
    {
        _logger.LogInformation("Setting up Production repository (Redis + SqlServer)");

        // Primary: Redis cache for hot lookups
        var redis = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        var primaryStore = new RedisUrlLookupStore(
            redis,
            config.StorageConfig,
            _serviceProvider.GetRequiredService<ILogger<RedisUrlLookupStore>>());

        // Secondary: SqlServer for reliable persistence
        var secondaryStore = new SqlServerUrlLookupStore(
            _serviceProvider.GetRequiredService<IDbContextFactory<UrlDbContext>>(),
            _serviceProvider.GetRequiredService<ILogger<SqlServerUrlLookupStore>>());

        return new CachedUrlRepository(
            primaryStore,
            secondaryStore,
            config.StorageConfig,
            _serviceProvider.GetRequiredService<ILogger<CachedUrlRepository>>());
    }

    /// <summary>
    /// Enterprise: DynamoDB (L2 database) + Redis (L1 cache)
    /// - AWS-native serverless architecture
    /// - Global tables for multi-region distribution
    /// - Automatic scaling without infrastructure management
    /// - Pay-per-request for predictable costs
    /// </summary>
    private IUrlRepository CreateEnterpriseRepository(UrlStorageConfiguration config)
    {
        _logger.LogInformation("Setting up Enterprise repository (DynamoDB + Redis)");

        // Primary: Redis cache for hot lookups
        var redis = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        var primaryStore = new RedisUrlLookupStore(
            redis,
            config.StorageConfig,
            _serviceProvider.GetRequiredService<ILogger<RedisUrlLookupStore>>());

        // Secondary: DynamoDB for serverless persistence
        var secondaryStore = new DynamoDbUrlLookupStore(
            _serviceProvider.GetRequiredService<IAmazonDynamoDB>(),
            config.StorageConfig,
            _serviceProvider.GetRequiredService<ILogger<DynamoDbUrlLookupStore>>());

        return new CachedUrlRepository(
            primaryStore,
            secondaryStore,
            config.StorageConfig,
            _serviceProvider.GetRequiredService<ILogger<CachedUrlRepository>>());
    }

    /// <summary>
    /// Archive: DynamoDB only
    /// - Single source of truth with automatic scaling
    /// - No cache layer for maximum consistency
    /// - Optimized for cost
    /// Best for: Cold data, occasional access, compliance scenarios
    /// </summary>
    private IUrlRepository CreateArchiveRepository(UrlStorageConfiguration config)
    {
        _logger.LogInformation("Setting up Archive repository (DynamoDB only)");

        var primaryStore = new DynamoDbUrlLookupStore(
            _serviceProvider.GetRequiredService<IAmazonDynamoDB>(),
            config.StorageConfig,
            _serviceProvider.GetRequiredService<ILogger<DynamoDbUrlLookupStore>>());

        return new CachedUrlRepository(
            primaryStore,
            secondaryStore: null,
            config.StorageConfig,
            _serviceProvider.GetRequiredService<ILogger<CachedUrlRepository>>());
    }

    /// <summary>
    /// Loads storage configuration from appsettings.
    /// Validates required properties for selected mode.
    /// </summary>
    private UrlStorageConfiguration GetStorageConfiguration()
    {
        var section = _configuration.GetSection("UrlStorage");

        var storageMode = section.GetValue<string>("Mode") ?? "Development";
        var cacheTtlMinutes = section.GetValue<int>("CacheTtlMinutes", 60);
        var writeThrough = section.GetValue<bool>("WriteThrough", true);

        ValidateConfiguration(storageMode, section);

        _logger.LogInformation($"Loaded storage config: Mode={storageMode}, TTL={cacheTtlMinutes}min, WriteThrough={writeThrough}");

        var modeEnum = storageMode switch
        {
            "Development" => UrlStorageMode.SqlServer,
            "Production" => UrlStorageMode.RedisCached,
            "Enterprise" => UrlStorageMode.DynamoDb,
            "Archive" => UrlStorageMode.DynamoDb,
            _ => throw new InvalidOperationException($"Unknown storage mode: {storageMode}")
        };

        return new UrlStorageConfiguration
        {
            StorageMode = storageMode,
            StorageConfig = new UrlStorageConfig
            {
                Mode = modeEnum,
                CacheTtlMinutes = cacheTtlMinutes,
                EnableRedisWriteThrough = writeThrough,
                DynamoDbTableName = section.GetValue<string>("TableName") ?? "url_shortcuts",
                SqlConnectionString = section.GetSection("SqlServer").GetValue<string>("ConnectionString"),
                RedisConnectionString = section.GetSection("Redis").GetValue<string>("ConnectionString"),
            }
        };
    }

    /// <summary>
    /// Validates that required configuration exists for the selected mode.
    /// </summary>
    private void ValidateConfiguration(string storageMode, IConfigurationSection section)
    {
        switch (storageMode)
        {
            case "Production":
                ValidateRedisConfig(section);
                ValidateSqlServerConfig(section);
                break;

            case "Enterprise":
                ValidateRedisConfig(section);
                ValidateDynamoDbConfig(section);
                break;

            case "Archive":
                ValidateDynamoDbConfig(section);
                break;

            case "Development":
                ValidateSqlServerConfig(section);
                break;

            default:
                throw new InvalidOperationException($"Unknown storage mode: {storageMode}");
        }
    }

    private void ValidateRedisConfig(IConfigurationSection section)
    {
        var redis = section.GetSection("Redis");
        if (string.IsNullOrEmpty(redis.GetValue<string>("ConnectionString")))
        {
            throw new InvalidOperationException(
                "Redis is required for this mode. " +
                "Configure UrlStorage:Redis:ConnectionString in appsettings.json");
        }
    }

    private void ValidateSqlServerConfig(IConfigurationSection section)
    {
        var sql = section.GetSection("SqlServer");
        if (string.IsNullOrEmpty(sql.GetValue<string>("ConnectionString")))
        {
            throw new InvalidOperationException(
                "SqlServer is required for this mode. " +
                "Configure UrlStorage:SqlServer:ConnectionString in appsettings.json");
        }
    }

    private void ValidateDynamoDbConfig(IConfigurationSection section)
    {
        var dynamo = section.GetSection("DynamoDB");
        if (string.IsNullOrEmpty(dynamo.GetValue<string>("TableName")))
        {
            throw new InvalidOperationException(
                "DynamoDB is required for this mode. " +
                "Configure UrlStorage:DynamoDB:TableName in appsettings.json");
        }
    }
}

/// <summary>
/// Configuration model for storage factory.
/// </summary>
public class UrlStorageConfiguration
{
    public required string StorageMode { get; set; }
    public required UrlStorageConfig StorageConfig { get; set; }
}

/// <summary>
/// Service collection extensions for storage registration.
/// </summary>
public static class UrlStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers URL storage infrastructure based on configuration.
    /// Automatically selects and configures appropriate stores.
    /// 
    /// Required configuration in appsettings.json:
    /// {
    ///   "UrlStorage": {
    ///     "Mode": "Production",
    ///     "CacheTtlMinutes": 60,
    ///     "WriteThrough": true,
    ///     "SqlServer": { "ConnectionString": "..." },
    ///     "Redis": { "ConnectionString": "..." },
    ///     "DynamoDB": { "TableName": "url_shortcuts" }
    ///   }
    /// }
    /// </summary>
    public static IServiceCollection AddUrlStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        string storageMode = "Development")
    {
        var storageConfig = configuration.GetSection("UrlStorage");
        var mode = storageConfig.GetValue<string>("Mode") ?? storageMode;

        switch (mode)
        {
            case "Production":
            case "Enterprise":
                // Add Redis for cache
                var redisConnectionString = storageConfig
                    .GetSection("Redis")
                    .GetValue<string>("ConnectionString");
                
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(redisConnectionString ??
                        throw new InvalidOperationException(
                            "Redis connection string not configured")));

                goto case "Archive"; // Fall through to add DynamoDB

            case "Archive":
                // Add DynamoDB - using direct client initialization
                services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
                break;

            case "Development":
                // Development just needs SQL Server (via existing DbContext)
                break;
        }

        // Always add the factory and repository
        services.AddScoped<UrlStorageFactory>();
        services.AddScoped<IUrlRepository>(sp =>
            sp.GetRequiredService<UrlStorageFactory>().CreateRepository());

        return services;
    }
}
