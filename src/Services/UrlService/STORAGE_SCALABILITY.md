# URL Storage Scalability - Multi-Tier Architecture

## Overview

The UrlShortener microservice now supports flexible, multi-tier storage allowing you to choose the right balance of performance, cost, and reliability for your deployment environment.

**Supported Topologies:**
- 🔧 **Development**: Single SQL Server store (simple, fast iteration)
- 🚀 **Production**: Redis cache + SQL Server persistence (millions of redirects/day)
- 🌐 **Enterprise**: Redis cache + DynamoDB serverless (auto-scaling, global distribution)
- 💾 **Archive**: DynamoDB only (cost-optimized, eventual consistency)

---

## Storage Modes

### 1. Development Mode
**Best for:** Local development, testing, debugging

```json
{
  "UrlStorage": {
    "Mode": "Development",
    "CacheTtlMinutes": 60,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=UrlDb;..."
    }
  }
}
```

**Characteristics:**
- Single SQL Server store (LocalDB in development)
- Direct database access for debugging
- No network latency
- Perfect for unit tests and integration tests
- ~50-100ms latency per request

**When to use:**
- Local development machine
- CI/CD pipeline (if using SQL Server in CI)
- Debugging storage issues
- Early prototyping

---

### 2. Production Mode
**Best for:** High-performance, high-volume deployments (millions of requests/day)

```json
{
  "UrlStorage": {
    "Mode": "Production",
    "CacheTtlMinutes": 60,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "Server=prod-sql.azure.com;Database=UrlDb;..."
    },
    "Redis": {
      "ConnectionString": "prod-redis.redis.cache.windows.net:6379,ssl=true",
      "Instance": 0
    }
  }
}
```

**Topology:**
```
Request → Redis Cache (L1)
            ↓ (miss)
         SQL Server (L2)
            ↓ (populate cache)
         Response
```

**Characteristics:**
- Redis provides sub-millisecond cache hits (~0.5-1ms)
- SQL Server provides reliable persistence
- Write-through strategy ensures consistency
- Automatic TTL expiration in Redis
- Database fallback on cache failures
- ~1ms average latency (cached), ~50ms (cache miss)

**Expected Performance:**
- Cache hit rate: 85-95% (typical URL redirect patterns)
- Throughput: 10,000-50,000 requests/second
- Memory usage: ~100MB per million cached URLs

**When to use:**
- Production SaaS deployment
- High-volume redirect service (>1M redirects/day)
- On-premises deployment with managed infrastructure
- Need for strong consistency and audit trails

**Configuration Tips:**
- Set `CacheTtlMinutes` based on URL expiration policy
  - Short TTL (30min): Better consistency, more cache misses
  - Long TTL (24h): Better performance, stale data risk
- Set `WriteThrough: true` to ensure all writes go to both stores
- Use Redis 6.0+ for better performance

---

### 3. Enterprise Mode
**Best for:** Serverless, globally distributed deployments

```json
{
  "UrlStorage": {
    "Mode": "Enterprise",
    "CacheTtlMinutes": 60,
    "WriteThrough": true,
    "Redis": {
      "ConnectionString": "enterprise-redis.redis.cache.windows.net:6379,ssl=true",
      "Instance": 0
    },
    "DynamoDB": {
      "TableName": "url_shortcuts",
      "Region": "us-east-1"
    }
  }
}
```

**Topology:**
```
Request → Redis Cache (L1, Regional)
            ↓ (miss)
         DynamoDB (L2, Global)
            ↓ (populate cache)
         Response
```

**DynamoDB Table Schema:**
```
Partition Key: ShortCode (String)
Sort Key: (None - optional for future versioning)

Attributes:
- OriginalUrl (String, Required)
- ShortCode (String, Partition Key)
- CreatedDate (Number, Unix timestamp)
- ExpirationDate (Number, Unix timestamp, TTL enabled)
- CreatedBy (String, GSI)
- IsDeleted (Boolean, optional)

Global Secondary Index:
- Index Name: CreatedBy-CreatedDate-Index
- Partition Key: CreatedBy
- Sort Key: CreatedDate
- (For user's URL listings)

TTL Configuration:
- Attribute Name: ExpirationDate
- Automatically delete expired items
```

**Characteristics:**
- AWS-native serverless architecture
- Automatic scaling without infrastructure management
- Global tables for multi-region distribution
- Built-in TTL for automatic cleanup
- Pay-per-request pricing model
- ~1ms average latency (cached), ~100ms (cache miss)
- Sub-second failover for disasters

**DynamoDB Pricing (Example):**
- 100M requests/month:
  - On-demand: ~$62 (if cached well)
  - Provisioned: ~$45 (if provisioned 1500 RCU/WCU)
- 1B requests/month:
  - On-demand: $620
  - Provisioned: $450

**When to use:**
- AWS deployments
- Need for serverless architecture
- Global distribution required
- Variable or unpredictable traffic patterns
- Cost optimization for high volume

**Setup Instructions:**

1. Create DynamoDB table in AWS Console:
```bash
aws dynamodb create-table \
  --table-name url_shortcuts \
  --attribute-definitions \
    AttributeName=ShortCode,AttributeType=S \
    AttributeName=CreatedBy,AttributeType=S \
    AttributeName=CreatedDate,AttributeType=N \
  --key-schema \
    AttributeName=ShortCode,KeyType=HASH \
  --billing-mode PAY_PER_REQUEST \
  --ttl-specification Enabled=true,AttributeName=ExpirationDate \
  --region us-east-1
```

2. Add GSI for user listings:
```bash
aws dynamodb update-table \
  --table-name url_shortcuts \
  --attribute-definitions AttributeName=CreatedBy,AttributeType=S AttributeName=CreatedDate,AttributeType=N \
  --global-secondary-index-updates \
    "Create={IndexName=CreatedBy-CreatedDate-Index,KeySchema=[{AttributeName=CreatedBy,KeyType=HASH},{AttributeName=CreatedDate,KeyType=RANGE}],Projection={ProjectionType=ALL},BillingMode=PAY_PER_REQUEST}" \
  --region us-east-1
```

3. Enable DynamoDB Streams for event sourcing (optional):
```bash
aws dynamodb update-table \
  --table-name url_shortcuts \
  --stream-specification StreamEnabled=true,StreamViewType=NEW_AND_OLD_IMAGES \
  --region us-east-1
```

---

### 4. Archive Mode
**Best for:** Cost-optimized, cold data, or compliance scenarios

```json
{
  "UrlStorage": {
    "Mode": "Archive",
    "CacheTtlMinutes": 0,
    "WriteThrough": false,
    "DynamoDB": {
      "TableName": "url_shortcuts_archive",
      "Region": "us-east-1"
    }
  }
}
```

**Characteristics:**
- Single DynamoDB store (no cache layer)
- Maximum consistency, no cache coherency issues
- Lowest operational overhead
- ~100-200ms latency per request
- Pay-per-request pricing
- Suitable for read-heavy, write-light scenarios
- Full audit trail

**When to use:**
- Cold data/archival storage
- Compliance requirements (no caching)
- Backup/disaster recovery storage
- Long-running batch processing
- Cost is primary concern

---

## Configuration Reference

### appsettings.json Template

```json
{
  "UrlStorage": {
    "Mode": "Development|Production|Enterprise|Archive",
    "CacheTtlMinutes": 60,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "Server=...;Database=UrlDb;..."
    },
    "Redis": {
      "ConnectionString": "host:port,ssl=true",
      "Instance": 0
    },
    "DynamoDB": {
      "TableName": "url_shortcuts",
      "Region": "us-east-1"
    }
  }
}
```

### Environment-Specific Files

**appsettings.Development.json:**
```json
{
  "UrlStorage": {
    "Mode": "Development",
    "CacheTtlMinutes": 30,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "Server=(localdb)\\MSSQLLocalDB;Database=UrlDb;..."
    }
  }
}
```

**appsettings.Staging.json:**
```json
{
  "UrlStorage": {
    "Mode": "Production",
    "CacheTtlMinutes": 60,
    "WriteThrough": true,
    "SqlServer": {
      "ConnectionString": "${SQL_CONNECTION_STRING}",
    },
    "Redis": {
      "ConnectionString": "${REDIS_CONNECTION_STRING}",
      "Instance": 0
    }
  }
}
```

**appsettings.Production.json:**
```json
{
  "UrlStorage": {
    "Mode": "Enterprise",
    "CacheTtlMinutes": 120,
    "WriteThrough": true,
    "Redis": {
      "ConnectionString": "${REDIS_CONNECTION_STRING}",
      "Instance": 0
    },
    "DynamoDB": {
      "TableName": "${DYNAMODB_TABLE_NAME}",
      "Region": "us-east-1"
    }
  }
}
```

---

## Migration Guide

### Migrating from Single-Store to Multi-Tier

#### Step 1: Update Program.cs
Already done! Just configure the Mode in appsettings.

#### Step 2: Update appsettings.json
Add the UrlStorage section with your chosen Mode.

#### Step 3: Initial Data Load
If migrating from SQL Server only to include Redis/DynamoDB:

```csharp
// In startup or migration job
var repository = serviceProvider.GetRequiredService<IUrlRepository>();
var urlService = serviceProvider.GetRequiredService<UrlShorteningService>();

// For Production mode: Warm cache
if (repository is CachedUrlRepository cachedRepo)
{
    var recentUrls = await urlService.GetRecentUrlsAsync(limit: 10000);
    await cachedRepo.BulkImportAsync(recentUrls);
}
```

#### Step 4: Verify Data Consistency
Monitor storage stores for 24 hours to ensure TTL, expiration, and write-through work correctly.

#### Step 5: Update Monitoring
Configure monitoring for the new storage backends (Redis hit rates, DynamoDB throttling, etc.)

---

## Performance Comparison

| Metric | Development | Production | Enterprise | Archive |
|--------|-------------|-----------|-----------|---------|
| Avg Latency (hit) | 50ms | 1ms | 1ms | 100ms |
| Avg Latency (miss) | 50ms | 50ms | 100ms | 100ms |
| Throughput | 100/sec | 10K/sec | 50K+/sec | 1K/sec |
| Consistency | Strong | Strong | Eventual* | Strong |
| Scaling | Manual | Manual | Auto | Auto |
| Cost | Low | Medium | Variable | Low |

*In Enterprise mode, Redis provides consistency for hot data; DynamoDB eventual consistency for cold data.

---

## Monitoring & Diagnostics

### Development
Monitor in SQL Server Management Studio or Azure Data Studio.

### Production
Monitor Redis and SQL Server:
```csharp
// Get cache stats (if using Redis)
if (repository is CachedUrlRepository cachedRepo)
{
    var stats = await cachedRepo.GetStorageStatsAsync();
    // { CacheHits: 9500, CacheMisses: 500, MemoryUsage: 125MB }
}
```

### Enterprise & Archive
Monitor DynamoDB in AWS Console:
- Consumed Read/Write Units
- Table Size
- TTL deletions
- Query latencies

### Key Metrics to Track

```
cache_hit_rate = (hits / (hits + misses)) * 100
goal: >85%

p99_latency for Production mode:
  - Cached: <5ms
  - Uncached: <100ms

p99_latency for Enterprise mode:
  - Cached: <5ms
  - DynamoDB: <150ms

DynamoDB throttling: 0 (alerts if >0)
```

---

## Troubleshooting

### High Latency with Production Mode
1. Check Redis connectivity and memory
   ```redis
   > INFO stats
   > MEMORY USAGE shortcode:xxx
   ```
2. Verify cache hit rate (should be >80%)
3. Check SQL Server query performance on cache misses
4. Increase Redis cache TTL if stale data is acceptable

### DynamoDB Throughput Exceeded
1. Check provisioned capacity vs. actual usage
2. Consider switching to on-demand billing
3. Review query patterns for optimization
4. Check for hot partitions (e.g., single user creating many URLs)

### Write-Through Consistency Issues
1. Monitor both store writes in logs
2. Enable CloudTrail for DynamoDB audit
3. Check for network partition between stores
4. Verify TTL values match across stores

### Cache Coherency Problems

If data is inconsistent between Redis and SQL Server:

1. **Immediate**: Set `CacheTtlMinutes: 5` to reduce inconsistency window
2. **Short-term**: Implement manual cache invalidation:
   ```csharp
   await redisStore.DeleteAsync(shortCode);
   await sqlStore.DeleteAsync(shortCode);
   ```
3. **Long-term**: Consider eventual consistency trade-offs

---

## Cost Optimization

### Production Mode (Redis + SQL)
- Cost increases linearly with volume
- Typical: $100-500/month for 100M requests
- Optimization: Tune Redis TTL to balance hit rate and memory

### Enterprise Mode (Redis + DynamoDB)
- DynamoDB on-demand pricing: ~$0.62 per million read units
- Typical: $50-200/month for 100M requests
- Optimization: Use provisioned capacity for predictable workloads

### Archive Mode (DynamoDB)
- Single store, no redundancy
- Typical: $30-100/month for 100M requests
- Optimization: Enable point-in-time recovery for compliance

---

## Best Practices

1. **Start with Development**, migrate to Production when needed
2. **Monitor cache hit rates** - aim for >85%
3. **Use shorter TTL (30min)** for data freshness, longer (24h) for performance
4. **Enable write-through** for strong consistency requirements
5. **Test failover** between primary and secondary stores
6. **Implement circuit breakers** for storage backend failures
7. **Log storage operations** for debugging
8. **Use compression** for large original URLs in Redis
9. **Implement rate limiting** at API layer, not storage layer
10. **Plan for data retention** - automatic TTL deletion in DynamoDB

---

## Related Documentation

- [SECRETS_MANAGEMENT.md](./SECRETS_MANAGEMENT.md) - Configuration management
- [AUTHENTICATION.md](./AUTHENTICATION.md) - Auth provider integration
- [Database Schema](./docs/SCHEMA.md) - ShortUrl model details
- [Performance Benchmarks](./docs/BENCHMARKS.md) - Real-world latency data
