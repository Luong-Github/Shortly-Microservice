# Observability Implementation Guide for UrlShortener

Your project is now configured for comprehensive observability. Here's how to use the resources I've created:

---

## 📚 Documentation Files Created

### 1. **OBSERVABILITY_GUIDE.md** (Comprehensive Reference)
- **Location:** `/UrlShortener/` root
- **Contains:**
  - Complete overview of Serilog + OpenTelemetry setup
  - How to use structured logging in your code
  - How to create custom traces with ActivitySource
  - Practical examples for each layer (Controller, Service, Repository, Event Publisher)
  - Best practices and error handling patterns
  - Configuration examples
  - How to view traces and logs

**When to use:** Read this for deep understanding of observability concepts

---

### 2. **TRACING_CHEATSHEET.md** (Quick Reference)
- **Location:** `/UrlShortener/` root
- **Contains:**
  - Copy-paste code patterns
  - Log levels quick guide
  - Common tag naming conventions
  - Full operation trace example
  - Program.cs configuration
  - View/debug tips
  - Error handling patterns

**When to use:** Reference this while coding to quickly find patterns

---

## 🚀 Quick Start: Add Observability to Your Code

### Step 1: Inject Logger (in any Service/Controller)

```csharp
private readonly ILogger<MyService> _logger;

public MyService(ILogger<MyService> logger)
{
    _logger = logger;
}
```

### Step 2: Log Business Events

```csharp
_logger.LogInformation("User created URL. ShortCode: {ShortCode}", shortCode);
```

### Step 3: Create ActivitySource (Module level)

```csharp
private static readonly ActivitySource ActivitySource = 
    new ActivitySource("UrlService.Services.MyService");
```

### Step 4: Trace Operations

```csharp
using var activity = ActivitySource.StartActivity("MyOperation");
activity?.SetTag("key", value);
activity?.AddEvent(new ActivityEvent("Something happened"));

// Do work...

if (error) activity?.SetStatus(ActivityStatusCode.Error, "message");
```

---

## 📊 Where OpenTelemetry is Already Configured

Your **Program.cs** already has:

```csharp
// ✅ Serilog configured
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/urlservice-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ✅ OpenTelemetry configured  
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()      // Auto-traces HTTP
        .AddHttpClientInstrumentation()      // Auto-traces outgoing HTTP
        .AddSource("UrlService.*")           // Your custom traces
        .AddConsoleExporter());              // Output to console
```

---

## 📍 Where to Add Tracing

### Controllers (API Endpoints)

```csharp
private static readonly ActivitySource ActivitySource = 
    new ActivitySource("UrlService.Controllers.UrlsController");

[HttpGet("{shortCode}")]
public async Task Redirect(string shortCode)
{
    using var activity = ActivitySource.StartActivity("GET /{shortCode}");
    activity?.SetTag("url.short_code", shortCode);
    
    try
    {
        var url = await _repository.GetByShortCodeAsync(shortCode);
        activity?.SetTag("found", url != null);
        return Redirect(url.LongUrl);
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

### Repositories

```csharp
private static readonly ActivitySource ActivitySource = 
    new ActivitySource("UrlService.Repositories.CachedUrlRepository");

public async Task<ShortUrl> GetByShortCodeAsync(string shortCode)
{
    using var activity = ActivitySource.StartActivity("GetByShortCodeAsync");
    activity?.SetTag("url.short_code", shortCode);
    
    try
    {
        // Try cache first
        using var cacheActivity = ActivitySource.StartActivity("CacheLookup");
        var cached = await _cache.GetStringAsync($"url:{shortCode}");
        
        if (cached != null)
        {
            cacheActivity?.SetTag("cache.status", "hit");
            _logger.LogInformation("Cache hit for {ShortCode}", shortCode);
            return JsonSerializer.Deserialize<ShortUrl>(cached);
        }
        
        // Query database
        cacheActivity?.SetTag("cache.status", "miss");
        var url = await _innerRepository.GetByShortCodeAsync(shortCode);
        
        if (url != null)
        {
            await _cache.SetStringAsync($"url:{shortCode}", JsonSerializer.Serialize(url),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) });
        }
        
        return url;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error retrieving URL {ShortCode}", shortCode);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

### Services

```csharp
private static readonly ActivitySource ActivitySource = 
    new ActivitySource("UrlService.Services.UrlShorteningService");

public async Task<ShortenedUrlResponse> ShortenUrlAsync(string longUrl)
{
    using var activity = ActivitySource.StartActivity("ShortenUrl");
    activity?.SetTag("url.length", longUrl.Length);
    
    try
    {
        // Generate short code
        using var generateActivity = ActivitySource.StartActivity("GenerateShortCode");
        var shortCode = GenerateShortCode();
        generateActivity?.SetTag("code", shortCode);
        
        // Save to repository
        using var saveActivity = ActivitySource.StartActivity("SaveUrl");
        await _repository.AddAsync(new ShortUrl { ShortCode = shortCode, LongUrl = longUrl });
        
        _logger.LogInformation("URL shortened: {ShortCode}", shortCode);
        return new ShortenedUrlResponse { ShortCode = shortCode };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error shortening URL");
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

### Event Publishers

```csharp
private static readonly ActivitySource ActivitySource = 
    new ActivitySource("UrlService.Events.ClickEventPublisher");

public async Task PublishClickEventAsync(string userId, Guid urlId)
{
    using var activity = ActivitySource.StartActivity("PublishClickEvent");
    activity?.SetTag("messaging.system", "rabbitmq");
    activity?.SetTag("event.user_id", userId);
    
    try
    {
        var factory = new ConnectionFactory { HostName = "localhost" };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        
        channel.QueueDeclare(queue: "click_events", durable: true);
        
        var message = JsonSerializer.Serialize(new { UserId = userId, UrlId = urlId });
        var body = Encoding.UTF8.GetBytes(message);
        
        channel.BasicPublish(exchange: "", routingKey: "click_events", body: body);
        
        _logger.LogInformation("Click event published for {UserId}", userId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to publish click event");
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

---

## 🔍 Viewing Traces & Logs

### 1. Console Output (During Running)
```
When you run `dotnet run`, you'll see:
- Log entries formatted with timestamps
- OpenTelemetry spans printed with ActivityID
- Real-time events as they happen
```

### 2. Log File
```
Logs are written to: logs/urlservice-YYYYMMDD.log
Read them:
  cat logs/urlservice-*.log
  
Search for errors:
  Select-String "error|ERROR" logs/urlservice-*.log
```

### 3. File Viewer During Development
```powershell
# Follow logs in real-time
Get-Content logs/urlservice-*.log -Wait -Tail 50

# Count errors
(Get-Content logs/urlservice-*.log | Select-String "ERROR").Count

# Search for specific shortCode
Select-String "abc123" logs/urlservice-*.log
```

---

## 📈 Monitoring Checklist

After adding traces to your code:

- [ ] Run `dotnet build` - should complete without errors
- [ ] Run `dotnet run` - should show OpenTelemetry startup messages
- [ ] Make a request to your API (e.g., POST /api/urls/shorten)
- [ ] Check console output for logs and traces
- [ ] Check `logs/` folder for persistent log files
- [ ] Search logs for your request ID to trace full flow
- [ ] Verify cache hit/miss patterns in logs
- [ ] Monitor database query latencies in spans

---

## 🎯 Next Steps

### Immediate (Add to existing code)

1. **Add traces to CachedUrlRepository.GetByShortCodeAsync()** 
   - See OBSERVABILITY_GUIDE.md for example
   - Pattern: Cache lookup span → Database lookup span → Cache write span

2. **Add traces to ClickEventPublisher**
   - Pattern: Connection → Channel → Queue declare → Publish

3. **Add logging to Controllers**
   - Log request details at start
   - Log success/errors with proper levels

### Short-term (Enhance observability)

4. **Add custom metrics** (success rate, latency percentiles)
5. **Configure Jaeger exporter** for visualization
6. **Add structured log sinks** (Seq, Application Insights)
7. **Set up alerts** on error rates or latency thresholds

### Long-term (Production-ready)

8. **Implement sampling** (don't trace 100% in production)
9. **Export to APM platform** (Datadog, New Relic, Azure Monitor)
10. **Create dashboards** for key metrics
11. **Set up SLIs/SLOs** for availability and latency

---

## 📋 Common Issues & Solutions

### Traces Not Appearing
- ✅ Check ActivitySource is registered in Program.cs with `.AddSource()`
- ✅ Verify class imports `using System.Diagnostics;`
- ✅ Confirm `ActivitySource` name matches registration
- ✅ Run in **Debug mode** with configuration set correctly

### Logs Not Appearing
- ✅ Check Serilog `MinimumLevel` (default is Information)
- ✅ Verify `builder.Host.UseSerilog()` is called
- ✅ Check `logs/` folder exists and is writable
- ✅ Ensure `ILogger<T>` is injected properly

### High Performance Overhead
- ✅ Logging too frequently? → Use Debug level or conditional logging
- ✅ Too many spans? → Use sampling in production
- ✅ Database queries slow? → Add caching or query optimization

---

## 📞 Quick Reference Links

- **OBSERVABILITY_GUIDE.md** - Full documentation
- **TRACING_CHEATSHEET.md** - Copy-paste patterns
- **Program.cs** - Configuration source of truth
- **Serilog Docs** - https://serilog.net/
- **OpenTelemetry Docs** - https://opentelemetry.io/

---

## Summary

**You now have:**
1. ✅ Serilog + OpenTelemetry fully configured in Program.cs
2. ✅ Complete observability guide with patterns
3. ✅ Cheatsheet for quick reference while coding
4. ✅ Working build system (ready to compile)

**To get started:**
1. Inject `ILogger<T>` into a service/controller
2. Call `_logger.LogInformation()` for your key events
3. Create `ActivitySource` at module level
4. Wrap operations with `using var activity = ActivitySource.StartActivity()`
5. Set tags with `activity?.SetTag()`
6. Run and watch logs in console or `logs/` folder

Questions? Review OBSERVABILITY_GUIDE.md or TRACING_CHEATSHEET.md!

