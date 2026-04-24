# OpenTelemetry + Serilog Quick Reference

## Quick Copy-Paste Patterns

### 1. Logging (Inject + Use)

```csharp
// Inject
private readonly ILogger<MyClass> _logger;

public MyClass(ILogger<MyClass> logger) => _logger = logger;

// Use
_logger.LogInformation("Simple message");
_logger.LogInformation("Message with {Property}", value);
_logger.LogWarning("Warning: {Code}", errorCode);
_logger.LogError(exception, "Error message: {Details}", details);
```

### 2. Create ActivitySource (Once per module)

```csharp
using System.Diagnostics;

namespace MyNamespace;

public class MyService
{
    private static readonly ActivitySource ActivitySource = 
        new ActivitySource("UrlService.Services.MyService");
        
    // Rest of class...
}
```

### 3. Create Spans (Wrap operations)

**Basic:**
```csharp
using var activity = ActivitySource.StartActivity("OperationName");

// Do work...

if (error) activity?.SetStatus(ActivityStatusCode.Error, "message");
```

**With tags:**
```csharp
using var activity = ActivitySource.StartActivity("GetUrl");
activity?.SetTag("url.id", id);
activity?.SetTag("cache.enabled", true);
```

**With events:**
```csharp
activity?.AddEvent(new ActivityEvent("Cache hit"));
activity?.AddEvent(new ActivityEvent("Data retrieved"));
```

**With exception:**
```csharp
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    activity?.RecordException(ex);
    throw;
}
```

### 4. Log Levels Quick Guide

```csharp
_logger.LogDebug("Variable x = {X}", x);                        // Dev only
_logger.LogInformation("Operation started");                    // Important events
_logger.LogWarning("Cache miss, using slower path");            // Unexpected but OK
_logger.LogError(ex, "Database error");                         // Error occurred
_logger.LogCritical("System failure");                          // System-level error
```

### 5. Common Tags (Standardized naming)

```
// HTTP
http.method = "GET", "POST"
http.url = "/api/urls/shorten"
http.status_code = 200, 404, 500
http.response.status_code = 201

// URL/Database
url.id = "guid-string"
url.short_code = "abc123"
url.length = 45
db.operation = "select", "insert", "update"
db.latency_ms = 125

// Cache
cache.status = "hit", "miss"
cache.hit = true/false
cache.key = "url:abc123"
cache.ttl_seconds = 86400

// Messaging
messaging.system = "rabbitmq"
messaging.destination = "click_events"
message.size_bytes = 256

// Error
error.type = "ArgumentException"
error.message = "Invalid input"
```

### 6. Logging Best Practices

```csharp
// ❌ DON'T
_logger.LogInformation($"User {userId} created {shortCode}");

// ✅ DO
_logger.LogInformation("User created short code", userId, shortCode);

// ✅ BETTER
_logger.LogInformation(
    "URL shortened. ShortCode: {ShortCode}, OriginalLength: {OriginalLength}, CompressionRatio: {Ratio:P}",
    shortCode,
    longUrl.Length,
    (double)shortCode.Length / longUrl.Length
);
```

### 7. Full Operation Trace Example

```csharp
public async Task<Url> GetByShortCodeAsync(string shortCode)
{
    // Root span
    using var activity = ActivitySource.StartActivity("GetByShortCodeAsync");
    activity?.SetTag("url.short_code", shortCode);

    try
    {
        // Child span: Cache
        using var cacheActivity = ActivitySource.StartActivity("CacheLookup");
        var cached = await _cache.GetStringAsync($"url:{shortCode}");
        
        if (cached != null)
        {
            cacheActivity?.SetTag("cache.status", "hit");
            activity?.SetTag("cache.hit", true);
            return JsonSerializer.Deserialize<Url>(cached);
        }

        // Child span: Database
        using var dbActivity = ActivitySource.StartActivity("RepositoryLookup");
        var url = await _repository.GetByShortCodeAsync(shortCode);
        dbActivity?.SetTag("result.found", url != null);
        
        if (url != null)
        {
            await _cache.SetStringAsync($"url:{shortCode}", JsonSerializer.Serialize(url),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });
        }

        activity?.SetTag("cache.hit", false);
        return url;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting URL {ShortCode}", shortCode);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        throw;
    }
}
```

### 8. Program.cs Configuration

```csharp
// Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ServiceName"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("MyService.*")        // Your custom ActivitySources
        .AddConsoleExporter());
```

### 9. View Traces & Logs

```powershell
# Real-time logs (tail)
Get-Content logs/app-20260419.log -Wait -Tail 20

# Search logs
Select-String "ERROR\|WARN" logs/app-*.log

# Total errors
(Select-String "\[ERR\]" logs/app-*.log).Count

# Grep for specific value
Select-String "abc123" logs/app-*.log | Select -First 10
```

### 10. Error Handling Pattern

```csharp
catch (ArgumentException ex)
{
    activity?.SetTag("error.type", "ArgumentException");
    activity?.SetTag("error.severity", "validation");
    activity?.SetStatus(ActivityStatusCode.Error, "Invalid argument");
    _logger.LogWarning(ex, "Validation error: {Message}", ex.Message);
    throw;
}
catch (InvalidOperationException ex)
{
    activity?.SetTag("error.type", "InvalidOperationException");
    activity?.SetTag("error.severity", "operational");
    activity?.SetStatus(ActivityStatusCode.Error, "Invalid operation");
    _logger.LogError(ex, "Operation failed: {Message}", ex.Message);
    throw;
}
catch (Exception ex)
{
    activity?.SetTag("error.type", ex.GetType().Name);
    activity?.SetTag("error.severity", "critical");
    activity?.SetStatus(ActivityStatusCode.Error, "Unexpected error");
    activity?.RecordException(ex);
    _logger.LogCritical(ex, "Critical error: {Message}", ex.Message);
    throw;
}
```

---

## Key Metrics to Track by Operation

### URL Shortening
```
- Duration (ms)
- Input URL length
- Collision rate
- ShortCode uniqueness check time
- Database insert time
- Cache write time
```

### URL Resolution
```
- Duration (ms)
- Cache hit ratio (%)
- Cache hit latency (ms)
- Database query latency (ms)
- Event publish latency (ms)
- Redirect destination validation time
```

### Click Event Publishing
```
- Duration (ms)
- Message size (bytes)
- RabbitMQ connection time (ms)
- Channel creation time (ms)
- Publish time (ms)
- Exception rate (%)
```

---

## Debug Checklist

🔍 **If traces aren't showing:**
- Is ActivitySource registered in Program.cs with `.AddSource()`?
- Is the ActivitySource name matching in your code?
- Run in **Debug mode** (check ActivityListener)?
- Check if `OpenTelemetry.Trace` namespace is imported?

🔍 **If logs aren't showing:**
- Is Serilog configured correctly in Program.cs?
- Check `MinimumLevel` (may filter out logs)?
- Is `UseSerilog()` called on `builder.Host`?
- Check that `logs/` directory has write permissions?

🔍 **If spans have no tags:**
- Verify `.SetTag()` is called before span ends?
- Tag values must be serializable (string, number, bool)?
- Check span is not already disposed?

---

## Export to External Services

### Jaeger (Dev Tracing)
```csharp
.AddJaegerExporter(opt =>
{
    opt.AgentHost = "localhost";
    opt.AgentPort = 6831;
})
// Visit: http://localhost:16686
```

### Seq (Log Viewer)
```csharp
.WriteTo.Seq("http://localhost:5341")
// Visit: http://localhost:5341
```

### Azure Application Insights
```csharp
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAzureMonitorTraceExporter());
```

