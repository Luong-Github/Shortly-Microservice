# UrlShortener: Observability Guide (OpenTelemetry + Serilog)

## Table of Contents
1. [Overview](#overview)
2. [Structured Logging with Serilog](#structured-logging-with-serilog)
3. [Distributed Tracing with OpenTelemetry](#distributed-tracing-with-opentelemetry)
4. [Practical Examples](#practical-examples)
5. [Best Practices](#best-practices)
6. [Viewing Traces & Logs](#viewing-traces--logs)

---

## Overview

Your UrlShortener uses two complementary observability tools:

| Tool | Purpose | Use Case |
|------|---------|----------|
| **Serilog** | Structured logging | Record events, debugging, performance metrics |
| **OpenTelemetry** | Distributed tracing | Track request flows, latency, dependencies |

**The Pyramid:**
```
User Request
    ↓
ASP.NET Core (auto-instrumented)
    ├─ HttpRequest (auto-traced)
    │   ├─ Controller (auto-traced)
    │   │   └─ Custom Business Logic (YOU instrument this)
    │   │       ├─ Database Query (auto-traced by EF Core instrumentation)
    │   │       ├─ Custom Span (you create)
    │   │       └─ Structured Logs (you emit)
    │   │
    │   └─ Response (auto-traced)
    │
    └─ Logs + Traces exported to Console/File
```

---

## Structured Logging with Serilog

### Current Configuration (Program.cs)

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()                    // Default: Info+
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)  // Suppress ASP.NET Core noise
    .Enrich.FromLogContext()                      // Add context data
    .Enrich.WithEnvironmentName()                 // Add environment
    .Enrich.WithThreadId()                        // Add thread ID
    .Enrich.WithProcessId()                       // Add processID
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File("logs/urlservice-.log", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();
```

### How to Use Serilog in Your Code

**1. Inject ILogger into Controllers/Services:**

```csharp
public class UrlsController : ControllerBase
{
    private readonly ILogger<UrlsController> _logger;
    private readonly UrlShorteningService _service;

    public UrlsController(ILogger<UrlsController> logger, UrlShorteningService service)
    {
        _logger = logger;
        _service = service;
    }

    [HttpPost("shorten")]
    public async Task<IActionResult> ShortenUrl([FromBody] ShortenUrlRequest request)
    {
        // Simple log
        _logger.LogInformation("Received URL shortening request for {OriginalUrl}", request.LongUrl);

        // Complex log with properties
        _logger.LogInformation(
            "Processing shorten request: {RequestId} from user {UserId} for domain {Domain}",
            HttpContext.TraceIdentifier,
            User.FindFirst("sub")?.Value,
            new Uri(request.LongUrl).Host
        );

        try
        {
            var result = await _service.ShortenUrlAsync(request.LongUrl);
            
            _logger.LogInformation(
                "URL shortened successfully. ShortCode: {ShortCode}, OriginalLength: {OriginalLength}, CompressedBy: {CompressionRatio:P}",
                result.ShortCode,
                request.LongUrl.Length,
                1 - (double)result.ShortCode.Length / request.LongUrl.Length
            );

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to shorten URL {Url} - {ErrorMessage}", request.LongUrl, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
    }
}
```

### Log Levels & When to Use

| Level | When to Use | Example |
|-------|------------|---------|
| **Debug** | Development only, detailed flow | `_logger.LogDebug("Entering method X with param {Param}", param);` |
| **Information** | Important business events | `_logger.LogInformation("URL created: {ShortCode}", code);` |
| **Warning** | Unexpected but recoverable | `_logger.LogWarning("Cache miss for key {Key}, hitting DB", key);` |
| **Error** | Errors that need attention | `_logger.LogError(ex, "Database connection failed");` |
| **Critical** | System-level failures | `_logger.LogCritical("Out of memory exception");` |

**Example in CachedUrlRepository:**

```csharp
public class CachedUrlRepository : IUrlRepository
{
    private readonly ILogger<CachedUrlRepository> _logger;
    private readonly IUrlRepository _innerRepository;
    private readonly IDistributedCache _cache;

    public CachedUrlRepository(
        ILogger<CachedUrlRepository> logger,
        IUrlRepository innerRepository,
        IDistributedCache cache)
    {
        _logger = logger;
        _innerRepository = innerRepository;
        _cache = cache;
    }

    public async Task<Url> GetByShortCodeAsync(string shortCode)
    {
        var cacheKey = $"url:{shortCode}";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Try cache first
            var cached = await _cache.GetStringAsync(cacheKey);
            stopwatch.Stop();

            if (cached != null)
            {
                _logger.LogInformation(
                    "Cache hit for ShortCode {ShortCode}. LookupTime: {LookupTimeMs}ms",
                    shortCode,
                    stopwatch.ElapsedMilliseconds
                );
                return JsonSerializer.Deserialize<Url>(cached);
            }

            _logger.LogWarning(
                "Cache miss for ShortCode {ShortCode}. Querying database after {LookupTimeMs}ms",
                shortCode,
                stopwatch.ElapsedMilliseconds
            );

            // Hit database
            var url = await _innerRepository.GetByShortCodeAsync(shortCode);
            
            if (url != null)
            {
                await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(url), 
                    new DistributedCacheEntryOptions 
                    { 
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) 
                    });

                _logger.LogInformation(
                    "URL retrieved from database and cached. ShortCode: {ShortCode}, CacheTTL: {TTLH}h",
                    shortCode,
                    24
                );
            }
            else
            {
                _logger.LogWarning("URL not found in database for ShortCode {ShortCode}", shortCode);
            }

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving URL for ShortCode {ShortCode}. Exception: {ExceptionMessage}",
                shortCode,
                ex.Message
            );
            throw;
        }
    }
}
```

### Structured Logging Best Practices

```csharp
// ❌ DON'T: String interpolation
_logger.LogInformation($"User {userId} created URL {shortCode}");

// ✅ DO: Named properties (enables searching/filtering)
_logger.LogInformation("User created URL", userId, shortCode);

// ✅ BETTER: Template with property names
_logger.LogInformation("User {UserId} created URL {ShortCode}", userId, shortCode);

// ✅ BEST: Rich context with related properties
_logger.LogInformation(
    "URL creation completed",
    new { UserId = userId, ShortCode = shortCode, LongUrlLength = longUrl.Length, CreatedAt = DateTime.UtcNow }
);
```

---

## Distributed Tracing with OpenTelemetry

### Current Configuration

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("UrlService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()              // Auto-traces HTTP requests
        .AddHttpClientInstrumentation()              // Auto-traces outgoing HTTP
        .AddConsoleExporter())                       // Export to console
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()              // Auto-records HTTP metrics
        .AddConsoleExporter());
```

### How to Create Custom Traces

**1. Activity Source (Define tracer):**

```csharp
using System.Diagnostics;

namespace UrlService.Tracing
{
    public static class TracingConfig
    {
        // Create activity source at module level
        public static readonly ActivitySource ActivitySource = 
            new ActivitySource("UrlService.Operations");
    }
}
```

**2. Use in Service/Repository:**

```csharp
using System.Diagnostics;
using UrlService.Tracing;

public class UrlShorteningService
{
    private readonly ILogger<UrlShorteningService> _logger;
    private readonly IUrlRepository _repository;

    public UrlShorteningService(ILogger<UrlShorteningService> logger, IUrlRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public async Task<ShortenedUrlResponse> ShortenUrlAsync(string longUrl)
    {
        // Create a span for this operation
        using var activity = TracingConfig.ActivitySource.StartActivity("ShortenUrl");
        
        // Add tags to the span (searchable metadata)
        activity?.SetTag("url.length", longUrl.Length);
        activity?.SetTag("url.domain", new Uri(longUrl).Host);
        activity?.SetTag("operation.type", "url_creation");

        try
        {
            _logger.LogInformation("Starting URL shortening for {Domain}", new Uri(longUrl).Host);

            // Generate short code
            using var generateActivity = TracingConfig.ActivitySource.StartActivity("GenerateShortCode");
            generateActivity?.SetTag("algorithm", "base62");
            
            var shortCode = GenerateShortCode();
            
            generateActivity?.SetTag("generated.code", shortCode);
            generateActivity?.AddEvent(new ActivityEvent("ShortCode generated"));

            // Validate uniqueness
            using var validateActivity = TracingConfig.ActivitySource.StartActivity("ValidateUniqueness");
            var existingUrl = await _repository.GetByShortCodeAsync(shortCode);
            
            if (existingUrl != null)
            {
                validateActivity?.SetTag("validation.result", "collision");
                _logger.LogWarning("Short code collision detected for {ShortCode}, retrying", shortCode);
                // Retry logic...
            }
            else
            {
                validateActivity?.SetTag("validation.result", "unique");
            }

            // Save to database
            using var saveActivity = TracingConfig.ActivitySource.StartActivity("SaveToRepository");
            var url = new Url 
            { 
                ShortCode = shortCode, 
                LongUrl = longUrl, 
                CreatedAt = DateTime.UtcNow 
            };
            
            await _repository.AddAsync(url);
            
            saveActivity?.SetTag("repository.operation", "insert");
            saveActivity?.SetTag("rows.affected", 1);

            _logger.LogInformation("URL shortened: {ShortCode} -> {LongUrl}", shortCode, longUrl);

            activity?.SetTag("result", "success");
            activity?.AddEvent(new ActivityEvent("URL successfully shortened"));

            return new ShortenedUrlResponse { ShortCode = shortCode };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error shortening URL {Url}", longUrl);
            
            activity?.SetTag("result", "error");
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            
            throw;
        }
    }

    private string GenerateShortCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }
}
```

### Tracing the Redirect Flow

```csharp
public class UrlsController : ControllerBase
{
    private readonly ILogger<UrlsController> _logger;
    private readonly IUrlRepository _repository;
    private readonly IClickEventPublisher _eventPublisher;

    [HttpGet("{shortCode}")]
    public async Task<IActionResult> Redirect(string shortCode)
    {
        // Create root span for entire redirect operation
        using var activity = TracingConfig.ActivitySource.StartActivity("Redirect");
        activity?.SetTag("http.method", HttpContext.Request.Method);
        activity?.SetTag("http.url", HttpContext.Request.Path);
        activity?.SetTag("url.short_code", shortCode);

        try
        {
            var startTime = System.Diagnostics.Stopwatch.StartNew();

            // Trace repository lookup
            using var lookupActivity = TracingConfig.ActivitySource.StartActivity("LookupUrl");
            var url = await _repository.GetByShortCodeAsync(shortCode);
            lookupActivity?.SetTag("repository.latency_ms", startTime.ElapsedMilliseconds);

            if (url == null)
            {
                activity?.SetTag("http.status_code", 404);
                activity?.AddEvent(new ActivityEvent("URL not found"));
                return NotFound();
            }

            lookupActivity?.SetTag("url.found", true);
            lookupActivity?.AddEvent(new ActivityEvent("URL resolved from storage"));

            // Trace click event publishing
            using var publishActivity = TracingConfig.ActivitySource.StartActivity("PublishClickEvent");
            publishActivity?.SetTag("messaging.system", "rabbitmq");
            publishActivity?.SetTag("messaging.destination", "click_events");

            var userId = User.FindFirst("sub")?.Value ?? "anonymous";
            await _eventPublisher.PublishClickEventAsync(userId, url.Id);

            publishActivity?.AddEvent(new ActivityEvent("Click event published"));

            // Create response
            activity?.SetTag("http.status_code", 301);
            activity?.SetTag("redirect.destination", url.LongUrl);
            activity?.AddEvent(new ActivityEvent($"Redirecting to {url.LongUrl}"));

            _logger.LogInformation(
                "Redirect completed. ShortCode: {ShortCode}, Destination: {Destination}, ElapsedMs: {ElapsedMs}",
                shortCode,
                url.LongUrl,
                startTime.ElapsedMilliseconds
            );

            return Redirect(url.LongUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error redirecting short code {ShortCode}", shortCode);
            
            activity?.SetTag("http.status_code", 500);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);

            throw;
        }
    }
}
```

---

## Practical Examples

### Example 1: CachedUrlRepository with Full Observability

```csharp
public class CachedUrlRepository : IUrlRepository
{
    private readonly ILogger<CachedUrlRepository> _logger;
    private readonly IUrlRepository _innerRepository;
    private readonly IDistributedCache _cache;
    private static readonly ActivitySource ActivitySource = 
        new ActivitySource("UrlService.Repositories.CachedUrlRepository");

    public async Task<Url> GetByShortCodeAsync(string shortCode)
    {
        using var activity = ActivitySource.StartActivity("GetByShortCodeAsync");
        activity?.SetTag("cache.key", $"url:{shortCode}");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // L1: Cache lookup
            using var cacheActivity = ActivitySource.StartActivity("CacheLookup");
            var cached = await _cache.GetStringAsync($"url:{shortCode}");
            stopwatch.Stop();

            if (cached != null)
            {
                cacheActivity?.SetTag("cache.status", "hit");
                cacheActivity?.SetTag("lookup.latency_ms", stopwatch.ElapsedMilliseconds);
                
                _logger.LogInformation(
                    "Cache hit for ShortCode {ShortCode}. Latency: {LatencyMs}ms",
                    shortCode,
                    stopwatch.ElapsedMilliseconds
                );

                activity?.SetTag("cache.hit", true);
                activity?.AddEvent(new ActivityEvent("Retrieved from cache"));

                return JsonSerializer.Deserialize<Url>(cached);
            }

            cacheActivity?.SetTag("cache.status", "miss");
            cacheActivity?.AddEvent(new ActivityEvent("Cache miss, querying repository"));

            // L2: Database lookup
            using var dbActivity = ActivitySource.StartActivity("RepositoryLookup");
            var dbStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var url = await _innerRepository.GetByShortCodeAsync(shortCode);
            dbStopwatch.Stop();

            dbActivity?.SetTag("database.latency_ms", dbStopwatch.ElapsedMilliseconds);

            // Cache if found
            if (url != null)
            {
                using var setCacheActivity = ActivitySource.StartActivity("SetCache");
                await _cache.SetStringAsync(
                    $"url:{shortCode}",
                    JsonSerializer.Serialize(url),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) }
                );
                setCacheActivity?.SetTag("cache.ttl_seconds", 86400);
            }

            activity?.SetTag("cache.hit", false);
            activity?.SetTag("total.latency_ms", stopwatch.Elapsed.TotalMilliseconds);

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting URL for shortCode {ShortCode}", shortCode);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }
}
```

### Example 2: ClickEventPublisher with Tracing

```csharp
public class ClickEventPublisher : IClickEventPublisher
{
    private readonly ILogger<ClickEventPublisher> _logger;
    private static readonly ActivitySource ActivitySource = 
        new ActivitySource("UrlService.Events.ClickEventPublisher");

    public async Task PublishClickEventAsync(string userId, Guid urlId)
    {
        using var activity = ActivitySource.StartActivity("PublishClickEvent");
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", "click_events");
        activity?.SetTag("event.user_id", userId);
        activity?.SetTag("event.url_id", urlId);

        try
        {
            var factory = new ConnectionFactory { HostName = "localhost" };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            using var declareActivity = ActivitySource.StartActivity("DeclareQueue");
            channel.QueueDeclare(
                queue: "click_events",
                durable: true,
                exclusive: false,
                autoDelete: false);
            declareActivity?.AddEvent(new ActivityEvent("Queue declared"));

            var clickEvent = new ClickEvent
            {
                UserId = userId,
                UrlId = urlId,
                Timestamp = DateTime.UtcNow
            };

            var message = JsonSerializer.Serialize(clickEvent);
            var body = Encoding.UTF8.GetBytes(message);

            using var publishActivity = ActivitySource.StartActivity("PublishMessage");
            channel.BasicPublish(
                exchange: "",
                routingKey: "click_events",
                basicProperties: null,
                body: body);
            publishActivity?.AddEvent(new ActivityEvent("Message published"));

            activity?.AddEvent(new ActivityEvent("Click event successfully published"));
            
            _logger.LogInformation(
                "Click event published. UserId: {UserId}, UrlId: {UrlId}, Message: {Message}",
                userId,
                urlId,
                message
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish click event for UserId {UserId} UrlId {UrlId}", userId, urlId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }
}
```

---

## Best Practices

### 1. **Property Naming Conventions**

```csharp
// Use semantic property names for better log queries
_logger.LogInformation(
    "Operation completed",
    new {
        // Request context
        RequestId = context.TraceIdentifier,
        UserId = user.Id,
        
        // Business metrics
        ShortCode = code,
        UrlLength = url.Length,
        
        // Performance metrics  
        DurationMs = stopwatch.ElapsedMilliseconds,
        CacheHit = cacheHit,
        
        // System context
        Environment = app.Environment.EnvironmentName,
        Version = Assembly.GetExecutingAssembly().GetVersion()
    }
);
```

### 2. **Exception Handling with Traces**

```csharp
try
{
    // Operation
}
catch (ArgumentException ex)
{
    activity?.SetTag("error.type", "ArgumentException");
    activity?.SetTag("error.validation", true);
    activity?.SetStatus(ActivityStatusCode.Error, "Invalid argument provided");
    activity?.RecordException(ex);
    
    _logger.LogWarning(ex, "Validation error: {ErrorMessage}", ex.Message);
    throw;
}
catch (Exception ex)
{
    activity?.SetTag("error.type", ex.GetType().Name);
    activity?.SetTag("error.severity", "critical");
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    activity?.RecordException(ex);
    
    _logger.LogError(ex, "Critical error occurred");
    throw;
}
```

### 3. **Creating Meaningful Spans**

```csharp
// ❌ DON'T: Activity for every line
using var activity1 = ActivitySource.StartActivity("SetVariable");
using var activity2 = ActivitySource.StartActivity("CheckCondition");
using var activity3 = ActivitySource.StartActivity("ReturnResult");

// ✅ DO: Activities for meaningful checkpoints
using var activity = ActivitySource.StartActivity("ProcessLargeDataSet");
using var initActivity = ActivitySource.StartActivity("InitializeResources");
// ... expensive operations...

using var processingActivity = ActivitySource.StartActivity("ProcessRecords");
// ... actual processing...

using var cleanupActivity = ActivitySource.StartActivity("Cleanup");
// ... cleanup...
```

### 4. **Correlation IDs**

```csharp
// Automatically available from ASP.NET Core
[HttpPost("process")]
public async Task<IActionResult> Process([FromBody] RequestDto request)
{
    var correlationId = HttpContext.TraceIdentifier;
    
    using var activity = ActivitySource.StartActivity("ProcessRequest");
    activity?.SetTag("correlation_id", correlationId);
    
    _logger.LogInformation(
        "Processing request with correlation ID {CorrelationId}",
        correlationId
    );

    // All spans under this activity will share the correlation ID
    return Ok();
}
```

---

## Viewing Traces & Logs

### 1. **Console Output (Development)**

When you run `dotnet run`, you'll see:

```
[15:30:45 INF] URL created successfully
[15:30:45 INF] {"ShortCode":"abc123","UserId":"user-456","ElapsedMs":125,"SourceContext":"UrlService.Handlers.CreateUrlCommandHandler"} 

Activity.Id=0hd3lrd50qr1go:1hqe05hs1g1qh0gg_g1
     SpanId:                 1hqe05hs1g1qh0gg_g1
     TraceId:                0hd3lrd50qr1go
     ParentId:               0hd3lrd50qr1go:1hqe05hs1g1qh0gg_g0
     ActivitySourceName:     UrlService.Operations
     DisplayName:            ShortenUrl
     StartTime:              2026-04-19T15:30:45.1234567Z
     Duration:               00:00:00.1250000
     TagObjects:
         url.length: 45
         url.domain: example.com
         operation.type: url_creation
     Events:\
         2026-04-19T15:30:45.2234567Z ShortCode generated
```

### 2. **View Logs File**

```powershell
# Follow log file in real-time
Get-Content logs/urlservice-20260419.log -Wait -Tail 50

# Search for specific shortCode
Select-String "abc123" logs/urlservice-*.log

# Count errors
(Select-String "\[ERR\]" logs/urlservice-*.log).Count
```

### 3. **Export to Observability Platform**

To export traces to Jaeger, Datadog, or Azure Application Insights, update `Program.cs`:

```csharp
// For Jaeger (local development)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddJaegerExporter(opt =>
        {
            opt.AgentHost = "localhost";
            opt.AgentPort = 6831;
        }));

// For Azure Application Insights
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddAzureMonitorTraceExporter());

// For Seq (structured log viewer)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Seq("http://localhost:5341")
    .CreateLogger();
```

### 4. **Query Traces**

Using `dotnet trace` tool:

```powershell
# Collect traces to file
dotnet trace collect -p <ProcessId> --output trace_file

# View with Visual Studio or PerfView
perfview trace_file.nettrace
```

---

## Configuration in appsettings.json

```json
{
  "Serilog": {
    "MinimumLevel": "Information",
    "MinimumLevel.Override": {
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },
  "OpenTelemetry": {
    "ServiceName": "UrlService",
    "TraceSamplingRate": 0.1,
    "Jaeger": {
      "Host": "localhost",
      "Port": 6831
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

---

## Quick Start Checklist

- [ ] Inject `ILogger<T>` into services/controllers
- [ ] Use `_logger.LogInformation()` for business events
- [ ] Create `ActivitySource` for complex operations
- [ ] Use `Activity.StartActivity()` to wrap business logic
- [ ] Set tags with `.SetTag("key", value)`
- [ ] Add events with `.AddEvent()`
- [ ] Handle exceptions with `.RecordException()`
- [ ] View console output during development
- [ ] Check `logs/` folder for persistent logs
- [ ] Export to Jaeger/Datadog for production

---

## Next Steps

1. **Add ActivitySource** to your repositories and services
2. **Instrument** ClickEventPublisher and UrlShorteningService
3. **Test** by running `dotnet run` and making requests
4. **Export** traces to Jaeger for visualization
5. **Monitor** logs in the `logs/` folder

