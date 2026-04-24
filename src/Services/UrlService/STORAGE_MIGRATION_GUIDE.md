# URL Storage Migration & Integration Guide

## Architecture Overview

The new multi-tier storage system provides flexible URL lookup across multiple backends while maintaining a consistent repository interface.

```
Application Layer
    ↓
IUrlRepository (CachedUrlRepository)
    ↓
Primary Store ←→ Secondary Store (Optional)
    ↓            ↓
Redis         SqlServer / DynamoDB
```

**Components:**

| Component | Purpose |
|-----------|---------|
| `IUrlRepository` | Business-facing interface (unchanged from user perspective) |
| `IUrlLookupStore` | Storage abstraction for individual backends |
| `CachedUrlRepository` | Multi-tier orchestration, fallback logic |
| `UrlStorageFactory` | Creates appropriate repository based on configuration |

---

## Migration Steps

### Step 1: Update Project Dependencies

Ensure your `.csproj` has these packages:

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
    <PackageReference Include="StackExchange.Redis" Version="2.6.0" />
    <PackageReference Include="AWSSDK.DynamoDBv2" Version="3.7.0" />
</ItemGroup>
```

### Step 2: Add Storage Configuration

Update `appsettings.json`:

```json
{
  "UrlStorage": {
    "Mode": "Development",
    "CacheTtlMinutes": 60,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "Server=...;Database=UrlDb;..."
    },
    "Redis": {
      "ConnectionString": "localhost:6379"
    },
    "DynamoDB": {
      "TableName": "url_shortcuts",
      "Region": "us-east-1"
    }
  }
}
```

### Step 3: Update Program.cs

**Before:**
```csharp
builder.Services.AddScoped<IUrlRepository, UrlRepository>();
```

**After:**
```csharp
var storageMode = builder.Configuration.GetValue<string>("UrlStorage:Mode") ?? "Development";
builder.Services.AddUrlStorage(builder.Configuration, storageMode);
```

### Step 4: Verify Existing Code Still Works

The `IUrlRepository` interface is **unchanged**:

```csharp
public interface IUrlRepository
{
    Task<ShortUrl?> GetByShortCodeAsync(string shortCode);
    Task<ShortUrl> CreateAsync(ShortUrl shortUrl);
    Task<List<ShortUrl>> GetAllByUserId(Guid userId);
}
```

All existing code using `IUrlRepository` continues to work without modification.

### Step 5: Environment-Specific Configuration

**Development** (`appsettings.Development.json`):
```json
{
  "UrlStorage": {
    "Mode": "Development",
    "CacheTtlMinutes": 30,
    "SqlServer": {
      "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=UrlDb;..."
    }
  }
}
```

**Production** (`appsettings.Production.json`):
```json
{
  "UrlStorage": {
    "Mode": "Production",
    "CacheTtlMinutes": 120,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "${SQL_CONNECTION_STRING}"
    },
    "Redis": {
      "ConnectionString": "${REDIS_CONNECTION_STRING}"
    }
  }
}
```

---

## Testing Integration

### Unit Test Example

```csharp
[TestClass]
public class CachedUrlRepositoryTests
{
    private MockUrlStore _primaryStore;
    private MockUrlStore _secondaryStore;
    private CachedUrlRepository _repository;

    [TestInitialize]
    public void Setup()
    {
        var config = new UrlStorageConfig 
        { 
            CacheTtlMinutes = 60,
            WriteThrough = true 
        };
        
        _primaryStore = new MockUrlStore();
        _secondaryStore = new MockUrlStore();
        
        var logger = LoggerFactory.Create(builder => builder.AddConsole())
            .CreateLogger<CachedUrlRepository>();
        
        _repository = new CachedUrlRepository(
            _primaryStore, 
            _secondaryStore, 
            config, 
            logger);
    }

    [TestMethod]
    public async Task GetByShortCode_ReturnsCacheHit()
    {
        // Arrange
        var shortUrl = new ShortUrl 
        { 
            ShortCode = "abc123",
            OriginalUrl = "https://example.com"
        };
        await _primaryStore.SetAsync(shortUrl);

        // Act
        var result = await _repository.GetByShortCodeAsync("abc123");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("https://example.com", result.OriginalUrl);
    }

    [TestMethod]
    public async Task GetByShortCode_FallsBackToSecondaryStore()
    {
        // Arrange
        var shortUrl = new ShortUrl 
        { 
            ShortCode = "xyz789",
            OriginalUrl = "https://other.com"
        };
        await _secondaryStore.SetAsync(shortUrl);

        // Act
        var result = await _repository.GetByShortCodeAsync("xyz789");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("https://other.com", result.OriginalUrl);
    }

    [TestMethod]
    public async Task CreateAsync_WritesToBothStores()
    {
        // Arrange
        var shortUrl = new ShortUrl 
        { 
            ShortCode = "write123",
            OriginalUrl = "https://writes.com"
        };

        // Act
        await _repository.CreateAsync(shortUrl);

        // Assert
        Assert.IsNotNull(await _primaryStore.GetByShortCodeAsync("write123"));
        Assert.IsNotNull(await _secondaryStore.GetByShortCodeAsync("write123"));
    }
}
```

### Integration Test Example

```csharp
[TestClass]
public class StorageIntegrationTests
{
    private IServiceProvider _serviceProvider;
    private IUrlRepository _repository;

    [TestInitialize]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "UrlStorage:Mode", "Development" },
                { "ConnectionStrings:UrlDbString", 
                  "Server=(localdb)\\MSSQLLocalDB;Database=UrlDb;..." }
            })
            .Build();

        services.AddDbContext<UrlDbContext>(opts =>
            opts.UseSqlServer(config.GetConnectionString("UrlDbString")));
        services.AddUrlStorage(config, "Development");
        services.AddLogging(builder => builder.AddConsole());

        _serviceProvider = services.BuildServiceProvider();
        _repository = _serviceProvider.GetRequiredService<IUrlRepository>();
    }

    [TestMethod]
    public async Task EndToEnd_CreateAndRetrieveUrl()
    {
        // Arrange
        var shortUrl = new ShortUrl
        {
            ShortCode = "inttest1",
            OriginalUrl = "https://integration.test",
            CreatedBy = Guid.NewGuid(),
            CreatedDate = DateTimeOffset.UtcNow
        };

        // Act
        await _repository.CreateAsync(shortUrl);
        var retrieved = await _repository.GetByShortCodeAsync("inttest1");

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("https://integration.test", retrieved.OriginalUrl);
    }
}
```

---

## Monitoring & Diagnostics

### Check Storage Configuration

```csharp
public class StorageHealthCheck : IHealthCheck
{
    private readonly IUrlRepository _repository;
    private readonly IConfiguration _config;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        var config = _config.GetSection("UrlStorage");
        var mode = config.GetValue<string>("Mode");

        // Test basic functionality
        try
        {
            var testUrl = new ShortUrl
            {
                ShortCode = $"health-{Guid.NewGuid()}",
                OriginalUrl = "https://health-check.test"
            };

            await _repository.CreateAsync(testUrl);
            var retrieved = await _repository.GetByShortCodeAsync(testUrl.ShortCode);

            return retrieved?.OriginalUrl == testUrl.OriginalUrl
                ? HealthCheckResult.Healthy($"Storage ({mode}) is healthy")
                : HealthCheckResult.Unhealthy($"Storage retrieval failed ({mode})");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Storage check failed: {ex.Message}");
        }
    }
}
```

### Register Health Check

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<StorageHealthCheck>("storage");

app.MapHealthChecks("/health");
```

### Logging Storage Operations

```csharp
public class StorageLogger : IUrlLookupStore
{
    private readonly IUrlLookupStore _inner;
    private readonly ILogger<StorageLogger> _logger;

    public async Task<ShortUrl?> GetByShortCodeAsync(string shortCode)
    {
        _logger.LogInformation($"[STORAGE] Getting: {shortCode}");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _inner.GetByShortCodeAsync(shortCode);
            _logger.LogInformation(
                $"[STORAGE] Got {shortCode} in {stopwatch.ElapsedMilliseconds}ms, " +
                $"Found={result != null}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[STORAGE] Error getting {shortCode}");
            throw;
        }
    }

    // ... implement other methods similarly
}
```

---

## Performance Tuning

### Development Mode
```json
{
  "UrlStorage": {
    "Mode": "Development",
    "CacheTtlMinutes": 30,
    "WriteThrough": true
  }
}
```
Optimizes for iteration speed and debugging clarity.

### Production Mode (High Volume)

```json
{
  "UrlStorage": {
    "Mode": "Production",
    "CacheTtlMinutes": 120,
    "WriteThrough": true
  }
}
```

**Tuning parameters:**
- Increase `CacheTtlMinutes` for stable URLs (24h expiry → 360 min TTL)
- Set `WriteThrough: false` if eventual consistency is acceptable
- Monitor Redis memory with `INFO MEMORY` command

### Enterprise Mode (Serverless)

```json
{
  "UrlStorage": {
    "Mode": "Enterprise",
    "CacheTtlMinutes": 60,
    "WriteThrough": true
  }
}
```

**DynamoDB sizing:**
```bash
# Estimate required capacity
read_capacity = peak_qps * 0.4  # 40% of peak for burst
write_capacity = peak_qps * 0.1  # 10% of peak for inserts
```

---

## Troubleshooting

### "Unknown storage mode" Error

**Cause:** Invalid or missing `UrlStorage:Mode` in configuration

**Solution:**
```json
{
  "UrlStorage": {
    "Mode": "Development"  // Must be one of: Development, Production, Enterprise, Archive
  }
}
```

### Redis Connection Timeout

**Cause:** Redis not running or connection string incorrect

**Solution:**
```bash
# Check Redis connectivity
redis-cli -h localhost -p 6379 ping

# Update connection string in appsettings
{
  "UrlStorage": {
    "Redis": {
      "ConnectionString": "localhost:6379,ssl=false"
    }
  }
}
```

### DynamoDB Throttling

**Cause:** Provisioned capacity exceeded

**Solution:**
1. Switch to on-demand billing:
   ```bash
   aws dynamodb update-table \
     --table-name url_shortcuts \
     --billing-mode PAY_PER_REQUEST
   ```

2. Or increase provisioned capacity:
   ```bash
   aws dynamodb update-table \
     --table-name url_shortcuts \
     --provisioned-throughput ReadCapacityUnits=1000,WriteCapacityUnits=1000
   ```

### Cache Coherency Issues

**Cause:** Data inconsistency between primary and secondary stores

**Solution:**
1. Reduce TTL: Set `CacheTtlMinutes: 5`
2. Manual invalidation:
   ```csharp
   var stores = serviceProvider.GetRequiredService<IEnumerable<IUrlLookupStore>>();
   foreach (var store in stores)
   {
       await store.DeleteAsync(shortCode);
   }
   ```
3. Full cache flush:
   ```bash
   redis-cli FLUSHDB
   ```

---

## API Compatibility

### Before (Old Repository)
```csharp
var repository = new UrlRepository(context);
var url = await repository.GetByShortCodeAsync("abc123");
```

### After (New Multi-Tier System)
```csharp
var repository = serviceProvider.GetRequiredService<IUrlRepository>();
var url = await repository.GetByShortCodeAsync("abc123");
```

**Behavior is identical** - no code changes needed in existing services!

---

## Next Steps

1. ✅ Configuration (appsettings.json)
2. ✅ Program.cs update
3. ✅ Verify existing tests pass
4. 🔄 Deploy to staging and monitor
5. 📊 Analyze performance and adjust configuration
6. 🚀 Deploy to production

---

## Related Documentation

- [STORAGE_SCALABILITY.md](./STORAGE_SCALABILITY.md) - Configuration & Modes
- [SECRETS_MANAGEMENT.md](./SECRETS_MANAGEMENT.md) - Credential Management
- [Database Schema](./docs/SCHEMA.md) - ShortUrl Model
