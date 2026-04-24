# URL Storage Quick Start Guide

Get up and running with the new multi-tier storage system in 5 minutes.

## Quick Setup

### 1. Verify Configuration (1 minute)

Your `appsettings.json` already has the storage configuration. Check that your mode is set:

```json
{
  "UrlStorage": {
    "Mode": "Development",
    "CacheTtlMinutes": 60,
    "WriteThrough": true
  }
}
```

### 2. Program.cs is Already Updated ✅

No manual changes needed! The storage factory is already registered:

```csharp
var storageMode = builder.Configuration.GetValue<string>("UrlStorage:Mode") ?? "Development";
builder.Services.AddUrlStorage(builder.Configuration, storageMode);
```

### 3. Start Using It (Immediate)

Your existing code works unchanged:

```csharp
// Inject as before
private readonly IUrlRepository _repository;

// Use exactly the same way
var shortUrl = await _repository.GetByShortCodeAsync("abc123");
await _repository.CreateAsync(new ShortUrl { ... });
```

---

## Common Scenarios

### I'm in Local Development

✅ You're already set! 

**Current setup:**
- Mode: `Development`
- Storage: SQL Server (LocalDB)
- No external dependencies
- Latency: ~50-100ms

**No changes needed.** Just run locally as usual.

### I'm Moving to Production

1. **Update configuration:**

```json
{
  "UrlStorage": {
    "Mode": "Production",
    "CacheTtlMinutes": 60,
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

2. **Set environment variables or secrets:**
   - SQL_CONNECTION_STRING: Your production SQL Server
   - REDIS_CONNECTION_STRING: Your Redis instance

3. **Deploy and monitor:**
   - Expected latency: 1-50ms
   - Monitor Redis hit rate (target >85%)

### I'm Using AWS

1. **Create DynamoDB table:**

```bash
aws dynamodb create-table \
  --table-name url_shortcuts \
  --attribute-definitions \
    AttributeName=ShortCode,AttributeType=S \
  --key-schema \
    AttributeName=ShortCode,KeyType=HASH \
  --billing-mode PAY_PER_REQUEST \
  --ttl-specification Enabled=true,AttributeName=ExpirationDate
```

2. **Update configuration:**

```json
{
  "UrlStorage": {
    "Mode": "Enterprise",
    "DynamoDB": {
      "TableName": "url_shortcuts",
      "Region": "us-east-1"
    },
    "Redis": {
      "ConnectionString": "${REDIS_CONNECTION_STRING}"
    }
  }
}
```

3. **Deploy:**
   - Expected latency: 1-100ms
   - Auto-scaling included
   - Global tables available

---

## Testing

### Quick Test

```csharp
var repository = serviceProvider.GetRequiredService<IUrlRepository>();

// Create
var shortUrl = new ShortUrl 
{ 
    ShortCode = "test123",
    OriginalUrl = "http://example.com",
    CreatedBy = Guid.NewGuid(),
    CreatedDate = DateTimeOffset.UtcNow
};
await repository.CreateAsync(shortUrl);

// Retrieve
var retrieved = await repository.GetByShortCodeAsync("test123");
Assert.AreEqual("http://example.com", retrieved.OriginalUrl);
```

### Health Check Endpoint

Add to your API:

```csharp
app.MapGet("/api/health/storage", async (IUrlRepository repo) =>
{
    try
    {
        var testCode = $"health-{Guid.NewGuid()}";
        await repo.CreateAsync(new ShortUrl 
        { 
            ShortCode = testCode, 
            OriginalUrl = "test" 
        });
        
        return Results.Ok("Storage is healthy");
    }
    catch
    {
        return Results.StatusCode(500);
    }
});
```

---

## Troubleshooting

### Error: "Unknown storage mode"
- ✅ **Solution:** Set `UrlStorage:Mode` to one of: `Development`, `Production`, `Enterprise`, `Archive`

### Error: "Redis connection string not configured"
- ✅ **Solution:** For Production/Enterprise modes, add Redis connection string in appsettings

### Slow Response Times
- ✅ **Development:** Expected 50-100ms
- ✅ **Production** (with cache): Expected 1-5ms for cache hits
- ✅ **Enterprise:** Expected 1-5ms for cache hits
- 🔧 If slower, check storage latencies: `redis-cli --latency`

### Cache Not Working
- ✅ **Redis Connectivity:** Test with `redis-cli PING`
- ✅ **Configuration:** Verify `Redis:ConnectionString` in appsettings
- ✅ **Logs:** Check application logs for storage errors

---

## Configuration Reference

### appsettings.Development.json
```json
{
  "UrlStorage": {
    "Mode": "Development",
    "CacheTtlMinutes": 30,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=UrlDb;Trusted_Connection=True;"
    }
  }
}
```

### appsettings.Production.json
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

### appsettings.Staging.json
```json
{
  "UrlStorage": {
    "Mode": "Production",
    "CacheTtlMinutes": 60,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "${SQL_STAGING_CONNECTION_STRING}"
    },
    "Redis": {
      "ConnectionString": "${REDIS_STAGING_CONNECTION_STRING}"
    }
  }
}
```

---

## Performance Expectations

| Scenario | Mode | Latency | Notes |
|----------|------|---------|-------|
| Local dev | Development | 50-100ms | Direct SQL Server |
| Production SaaS | Production | 1-50ms | Redis + SQL Server |
| AWS deployment | Enterprise | 1-100ms | Redis + DynamoDB |
| Compliance | Archive | 100-150ms | DynamoDB only |

---

## What's Included

### Files Created
- `/Storage/IUrlLookupStore.cs` - Storage interface
- `/Storage/CachedUrlRepository.cs` - Multi-tier orchestrator  
- `/Storage/UrlStorageFactory.cs` - Factory & configuration
- `/Storage/RedisUrlLookupStore.cs` - Redis implementation
- `/Storage/SqlServerUrlLookupStore.cs` - SQL Server implementation
- `/Storage/DynamoDbUrlLookupStore.cs` - DynamoDB implementation

### Files Updated
- `Program.cs` - Storage registration
- `appsettings.json` - Configuration section

### Documentation
- `STORAGE_SCALABILITY.md` - Complete reference
- `STORAGE_MIGRATION_GUIDE.md` - Integration guide
- `STORAGE_PHASE3_SUMMARY.md` - Phase completion summary
- `STORAGE_QUICKSTART.md` - This file

---

## Next Steps

1. ✅ **Configuration** - Already in place
2. ✅ **Program.cs** - Already updated
3. ✅ **Dependencies** - Already registered
4. 🔄 **Testing** - Run existing tests to verify
5. 📊 **Monitoring** - Set up health checks
6. 🚀 **Deployment** - Deploy to staging first

---

## Need Help?

- **Configuration questions:** See [STORAGE_SCALABILITY.md](./STORAGE_SCALABILITY.md#configuration-reference)
- **Integration details:** See [STORAGE_MIGRATION_GUIDE.md](./STORAGE_MIGRATION_GUIDE.md)
- **Architecture overview:** See [STORAGE_PHASE3_SUMMARY.md](./STORAGE_PHASE3_SUMMARY.md)
- **Troubleshooting:** See [STORAGE_SCALABILITY.md#troubleshooting](./STORAGE_SCALABILITY.md#troubleshooting)

**Your existing code needs NO changes.** The repository interface is unchanged!
