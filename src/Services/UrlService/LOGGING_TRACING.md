# Structured Logging & Tracing - UrlService

## Overview

The UrlService now includes comprehensive structured logging and distributed tracing for observability and debugging. This enables:

- **Structured Logging**: JSON-formatted logs with searchable properties
- **Distributed Tracing**: End-to-end request tracing across services
- **Performance Monitoring**: Latency tracking and bottleneck identification
- **Error Correlation**: Linking logs and traces for debugging

---

## Components

### 1. Serilog (Structured Logging)

**Features:**
- JSON-formatted logs with structured properties
- Multiple sinks (Console, File, potentially external systems)
- Rich context enrichment (thread ID, machine name, etc.)
- Configurable log levels per namespace

**Configuration:**
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "UrlService.Storage": "Debug"
      }
    },
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"],
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/urlservice-.log",
          "rollingInterval": "Day"
        }
      }
    ]
  }
}
```

### 2. OpenTelemetry (Distributed Tracing)

**Features:**
- ASP.NET Core instrumentation (automatic)
- HTTP client instrumentation
- Custom activity tracing in storage operations
- Console exporter (development)
- Extensible to Jaeger, Zipkin, Application Insights

**Configuration:**
```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("UrlService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());
```

---

## Log Structure

### Structured Log Example

```
[14:23:15 INF] Retrieved short code abc123 from primary store Redis in 2ms {"ShortCode":"abc123","StoreName":"Redis","ElapsedMs":2,"UserId":"user-123"}
```

**Properties:**
- `ShortCode`: The URL short code being accessed
- `StoreName`: Which storage backend served the request
- `ElapsedMs`: Operation duration in milliseconds
- `UserId`: User who created the URL (when available)

### Log Levels

| Level | Usage |
|-------|-------|
| `Trace` | Detailed execution flow |
| `Debug` | Internal state changes |
| `Information` | Normal operations (cache hits, creations) |
| `Warning` | Recoverable issues (cache misses, secondary store failures) |
| `Error` | Operation failures |
| `Critical` | System-level failures |

---

## Tracing Structure

### Activity Hierarchy

```
Request (ASP.NET Core)
├── GetByShortCode (CachedUrlRepository)
│   ├── Redis.GetByShortCode (RedisUrlLookupStore)
│   └── SqlServer.GetByShortCode (SqlServerUrlLookupStore) [if miss]
└── CreateShortUrl (CachedUrlRepository)
    ├── Redis.Set (RedisUrlLookupStore)
    └── SqlServer.Set (SqlServerUrlLookupStore) [async]
```

### Trace Tags

**Common Tags:**
- `shortCode`: URL identifier
- `userId`: User who owns the URL
- `store`: Storage backend (Redis, SqlServer, DynamoDB)
- `result`: Operation outcome (hit, miss, success, failed)
- `duration_ms`: Operation duration
- `error`: Error message (if applicable)

**Storage-Specific Tags:**
- `cacheTtlMinutes`: Cache expiration time
- `writeThrough`: Whether secondary writes are enabled
- `ttlRemaining`: Time until cache expiration

---

## Storage Operation Logs

### CachedUrlRepository

**GetByShortCodeAsync:**
```
INF Retrieved short code {ShortCode} from primary store {StoreName} in {ElapsedMs}ms
WRN Short code {ShortCode} not found in any store after {ElapsedMs}ms
ERR Error retrieving short code {ShortCode} after {ElapsedMs}ms
```

**CreateAsync:**
```
INF Created short URL {ShortCode} for user {UserId} in primary store {StoreName} in {ElapsedMs}ms
INF Async write completed for short code {ShortCode} to secondary store {StoreName}
WRN Secondary store write failed for short code {ShortCode} in store {StoreName}
ERR Error creating short URL {ShortCode} for user {UserId} after {ElapsedMs}ms
```

### RedisUrlLookupStore

**GetByShortCodeAsync:**
```
INF Redis cache HIT for shortCode {ShortCode} in {ElapsedMs}ms, TTL remaining: {TtlRemaining}s
DBG Redis cache MISS for shortCode {ShortCode} in {ElapsedMs}ms
ERR Error retrieving from Redis for shortCode {ShortCode} after {ElapsedMs}ms
```

**SetAsync:**
```
INF Cached shortCode {ShortCode} for user {UserId} in Redis with TTL {TtlMinutes}min in {ElapsedMs}ms
ERR Error caching shortCode {ShortCode} to Redis after {ElapsedMs}ms
```

---

## Monitoring & Alerting

### Key Metrics to Monitor

**Performance:**
- Cache hit rate: `cache_hits / (cache_hits + cache_misses)`
- Average latency per operation
- P95/P99 latency percentiles

**Errors:**
- Storage operation failures
- Cache connection issues
- Secondary store write failures

**Business:**
- URLs created per minute
- Redirect requests per minute
- Top accessed short codes

### Sample Queries (ELK Stack)

**Cache Performance:**
```kql
index:urlservice level:info message:"cache HIT" OR message:"cache MISS"
| stats count() by message
| sort -count
```

**Slow Operations:**
```kql
index:urlservice level:info ElapsedMs > 100
| table @timestamp, message, ElapsedMs, ShortCode
| sort -ElapsedMs
```

**Error Correlation:**
```kql
index:urlservice level:error OR level:critical
| stats count() by ShortCode, message
| sort -count
```

---

## Configuration Options

### Development Environment

**appsettings.Development.json:**
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "UrlService.Storage": "Debug"
      }
    },
    "WriteTo": [
      {
        "Name": "Console"
      }
    ]
  }
}
```

### Production Environment

**appsettings.Production.json:**
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "/var/log/urlservice/urlservice-.log"
        }
      },
      {
        "Name": "Elasticsearch",
        "Args": {
          "nodeUris": "http://elasticsearch:9200"
        }
      }
    ]
  }
}
```

---

## Extending Tracing

### Adding Custom Activities

```csharp
using var activity = ActivitySource.StartActivity("CustomOperation", ActivityKind.Internal);
activity?.SetTag("customProperty", value);

// Your operation here

activity?.SetTag("result", "success");
activity?.SetTag("duration_ms", stopwatch.ElapsedMilliseconds);
```

### Adding Custom Log Properties

```csharp
_logger.LogInformation(
    "Operation completed {OperationName} for {EntityType} {EntityId}",
    operationName,
    entityType,
    entityId);
```

### Instrumenting External Calls

```csharp
using var activity = ActivitySource.StartActivity("ExternalApiCall", ActivityKind.Client);
activity?.SetTag("api.endpoint", endpoint);
activity?.SetTag("api.method", method);

// Make HTTP call

activity?.SetTag("http.status_code", response.StatusCode);
```

---

## Troubleshooting

### High Latency Issues

1. **Check cache hit rate:**
   ```bash
   grep "cache HIT\|cache MISS" logs/urlservice-*.log | tail -20
   ```

2. **Identify slow operations:**
   ```bash
   grep "ElapsedMs.*[0-9]\{3,\}" logs/urlservice-*.log | tail -10
   ```

3. **Monitor Redis performance:**
   ```bash
   redis-cli --latency
   redis-cli INFO stats
   ```

### Log Not Appearing

1. **Check log level configuration**
2. **Verify Serilog configuration in appsettings.json**
3. **Ensure logger injection in constructor**

### Tracing Not Working

1. **Verify OpenTelemetry packages installed**
2. **Check activity source name matches**
3. **Ensure ActivitySource is static readonly**

---

## Best Practices

### Logging
1. **Use structured properties** instead of string interpolation
2. **Include relevant context** (user ID, operation ID, etc.)
3. **Log at appropriate levels** (Info for normal ops, Debug for details)
4. **Avoid sensitive data** in logs

### Tracing
1. **Use meaningful activity names** (e.g., "Redis.GetByShortCode")
2. **Set relevant tags** for filtering and analysis
3. **Measure operation duration** with Stopwatch
4. **Handle exceptions** in activities

### Performance
1. **Use async logging** to avoid blocking
2. **Batch log writes** when possible
3. **Configure appropriate log levels** in production
4. **Monitor log volume** and storage costs

---

## Related Documentation

- [STORAGE_SCALABILITY.md](./STORAGE_SCALABILITY.md) - Storage configuration
- [STORAGE_MIGRATION_GUIDE.md](./STORAGE_MIGRATION_GUIDE.md) - Integration guide
- [Serilog Documentation](https://github.com/serilog/serilog/wiki)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/net/)
