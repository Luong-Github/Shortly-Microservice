# Production-Ready UrlShortener: Tool Stack & Implementation Guide

## The Complete Picture 🎯

You want to run UrlShortener as a **live production app** with visibility into every aspect. Here's what you need and what to implement:

---

## 🏗️ Production Stack Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     YOUR APPLICATION                            │
│  UrlShortener (ASP.NET Core 8)                                 │
│  ├─ Health Checks Endpoint                                     │
│  ├─ Structured Logging (Serilog) → Sink                       │
│  ├─ OpenTelemetry Traces → Exporter                           │
│  └─ Metrics → Exporter                                         │
└────────────┬────────────────────────────────────────────────────┘
             │
    ┌────────┴────────┐
    │                 │
┌───▼────────┐   ┌───▼──────────┐
│   LOGS     │   │   TRACES     │
│  (Serilog) │   │ (OTel)       │
└───┬────────┘   └───┬──────────┘
    │                 │
    ├─ Console       ├─ Console
    ├─ File          ├─ Jaeger
    ├─ Seq           ├─ AzAppInsights
    └─ Splunk        └─ Datadog


DEPLOYMENT LAYER:
├─ Docker (Containerization)
├─ Kubernetes / Docker Compose (Orchestration)
├─ Azure Container Registry (Image Storage)
└─ Azure App Service / ACI (Hosting)

MONITORING LAYER:
├─ Health Check Endpoint (/health)
├─ Application Insights (APM)
├─ Azure Monitor (Metrics/Alerts)
└─ Custom Dashboard (Grafana)

CI/CD PIPELINE:
├─ Git (Source Control)
├─ GitHub Actions (Build/Test)
├─ Azure Container Registry (Image Push)
└─ Azure App Service (Deploy)
```

---

## 📋 Implementation Roadmap

### **PHASE 1: Health & Diagnostics** (Week 1)
✅ = Critical for production

- [ ] **Health Check Endpoint** - `/health` - ASP.NET Core built-in
- [ ] **Structured Logging Export** - Serilog sink to file/Seq
- [ ] **OpenTelemetry Export** - Console → Jaeger/Application Insights
- [ ] **Dockerfile** - Containerize app
- [ ] **Docker Compose** - Local multi-container setup (App + Redis + SQL + RabbitMQ)

### **PHASE 2: Observability** (Week 2)
- [ ] **Application Insights Setup** - Azure APM
- [ ] **Custom Metrics** - Request rates, cache hit ratio, latency
- [ ] **Alerts** - Error rate, high latency thresholds
- [ ] **Logging Aggregation** - Seq or Splunk
- [ ] **Jaeger Tracing** - Visualize request flows

### **PHASE 3: Deployment** (Week 3)
- [ ] **CI/CD Pipeline** - GitHub Actions + Azure
- [ ] **Azure Container Registry** - Image storage
- [ ] **Azure App Service** - Host the app
- [ ] **Database Migration** - Azure SQL Server
- [ ] **Environment Configuration** - Dev/Staging/Production

### **PHASE 4: Operations** (Week 4)
- [ ] **Custom Dashboard** - Grafana/Power BI
- [ ] **Runbooks** - Troubleshooting guides
- [ ] **Load Testing** - Performance baseline
- [ ] **Backup Strategy** - Database snapshots
- [ ] **Incident Response** - Alert rules + on-call

---

## 🎯 What to Implement in YOUR SOURCE CODE

### **1. HEALTH CHECK ENDPOINT** (15 minutes)

**File:** `src/Services/UrlService/Health/HealthController.cs`

```csharp
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UrlService.Health;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    private readonly IHealthCheckService _healthCheckService;

    public HealthController(IHealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var result = await _healthCheckService.CheckHealthAsync();
        return result.Status == HealthStatus.Healthy ? Ok(result) : StatusCode(503, result);
    }
}
```

**Register in Program.cs:**

```csharp
// Add health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("UrlDbString"), 
        name: "SqlServer", 
        tags: new[] { "db" })
    .AddRedis(builder.Configuration["Cache:RedisConnection"], 
        name: "Redis", 
        tags: new[] { "cache" })
    .AddRabbitMQ(new Uri("amqp://localhost:5672"), 
        name: "RabbitMQ", 
        tags: new[] { "messaging" });

// Map health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions 
{ 
    Predicate = reg => reg.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions 
{ 
    Predicate = reg => reg.Tags.Contains("ready")
});
```

**Output:** 
```json
GET /health →
{
  "status": "Healthy",
  "checks": {
    "SqlServer": { "status": "Healthy", "duration": "45ms" },
    "Redis": { "status": "Healthy", "duration": "3ms" },
    "RabbitMQ": { "status": "Healthy", "duration": "12ms" }
  }
}
```

---

### **2. CONTAINERIZATION WITH DOCKER** (30 minutes)

**File:** `Dockerfile` (Project Root)

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /src

COPY ["src/Services/UrlService/UrlService.csproj", "src/Services/UrlService/"]
COPY ["src/Shared/", "src/Shared/"]
COPY ["src/Infrastructure/", "src/Infrastructure/"]

RUN dotnet restore "src/Services/UrlService/UrlService.csproj"

COPY . .
RUN dotnet build "src/Services/UrlService/UrlService.csproj" -c Release -o /app/build

# Publish stage
FROM builder AS publish
RUN dotnet publish "src/Services/UrlService/UrlService.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "UrlService.dll"]
```

**File:** `docker-compose.yml` (Project Root)

```yaml
version: '3.8'

services:
  urlservice:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ConnectionStrings__UrlDbString=Server=sqlserver;Database=UrlShortener;User Id=sa;Password=YourPassword123!
      - Cache__RedisConnection=redis:6379
      - RabbitMQ__Host=rabbitmq
      - ASPNETCORE_ENVIRONMENT=Development
    depends_on:
      - sqlserver
      - redis
      - rabbitmq
    networks:
      - urlshortener-network
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 5s

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    ports:
      - "1433:1433"
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=YourPassword123!
    volumes:
      - sqldata:/var/opt/mssql
    networks:
      - urlshortener-network

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    networks:
      - urlshortener-network
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 3

  rabbitmq:
    image: rabbitmq:3.12-management-alpine
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      - RABBITMQ_DEFAULT_USER=guest
      - RABBITMQ_DEFAULT_PASS=guest
    networks:
      - urlshortener-network
    healthcheck:
      test: rabbitmq-diagnostics -q ping
      interval: 10s
      timeout: 5s
      retries: 3

volumes:
  sqldata:

networks:
  urlshortener-network:
    driver: bridge
```

**Build & Run:**
```bash
docker-compose up --build
```

---

### **3. LOGGING AGGREGATION - EXPORT TO SEQ** (20 minutes)

**Install:** `dotnet add package Serilog.Sinks.Seq`

**Update Program.cs:**

```csharp
// Configure Serilog with multiple sinks
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("Application", "UrlService")
    .WriteTo.Console()
    .WriteTo.File("logs/urlservice-.log", rollingInterval: RollingInterval.Day)
    // NEW: Export to Seq for log aggregation
    .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341")
    .CreateLogger();
```

**Update appsettings.json:**

```json
{
  "Seq": {
    "ServerUrl": "http://localhost:5341"
  }
}
```

**Add to docker-compose.yml:**

```yaml
seq:
  image: datalust/seq:latest
  ports:
    - "5341:5341"
  environment:
    - ACCEPT_EULA=Y
  networks:
    - urlshortener-network
```

**Access:** http://localhost:5341

---

### **4. OPENTELEMETRY EXPORT - JAEGER** (20 minutes)

**Install:** `dotnet add package OpenTelemetry.Exporter.Jaeger`

**Update Program.cs:**

```csharp
// Add Jaeger exporter
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("UrlService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("UrlService.*")
        // NEW: Export to Jaeger
        .AddJaegerExporter(opt =>
        {
            opt.AgentHost = builder.Configuration["Jaeger:AgentHost"] ?? "localhost";
            opt.AgentPort = int.Parse(builder.Configuration["Jaeger:AgentPort"] ?? "6831");
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());
```

**Add to docker-compose.yml:**

```yaml
jaeger:
  image: jaegertracing/all-in-one:latest
  ports:
    - "16686:16686"    # UI
    - "6831:6831/udp"  # Agent
    - "14268:14268"    # Collector
  networks:
    - urlshortener-network
```

**Access:** http://localhost:16686

---

### **5. METRICS & CUSTOM DIAGNOSTICS** (30 minutes)

**File:** `src/Services/UrlService/Services/MetricsService.cs`

```csharp
using System.Diagnostics.Metrics;

namespace UrlService.Services;

public class MetricsService
{
    private static readonly Meter Meter = new Meter("UrlService.Metrics", "1.0.0");
    
    // Counters
    public static readonly Counter<int> UrlsCreatedCounter = 
        Meter.CreateCounter<int>("urls.created.total", description: "Total URLs created");
    
    public static readonly Counter<int> UrlsAccessedCounter = 
        Meter.CreateCounter<int>("urls.accessed.total", description: "Total URL redirects");
    
    public static readonly Counter<int> CacheHitsCounter = 
        Meter.CreateCounter<int>("cache.hits.total", description: "Cache hits");
    
    public static readonly Counter<int> CacheMissesCounter = 
        Meter.CreateCounter<int>("cache.misses.total", description: "Cache misses");
    
    // Histograms (latency)
    public static readonly Histogram<double> UrlResolutionLatency = 
        Meter.CreateHistogram<double>("url.resolution.ms", description: "URL resolution latency in milliseconds");
    
    public static readonly Histogram<double> DatabaseLatency = 
        Meter.CreateHistogram<double>("db.query.ms", description: "Database query latency in milliseconds");
}
```

**Use in Repository:**

```csharp
public async Task<ShortUrl> GetByShortCodeAsync(string shortCode)
{
    using var activity = ActivitySource.StartActivity("GetByShortCodeAsync");
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        var cached = await _cache.GetStringAsync($"url:{shortCode}");
        
        if (cached != null)
        {
            MetricsService.CacheHitsCounter.Add(1);
            MetricsService.UrlAccessedCounter.Add(1);
            MetricsService.UrlResolutionLatency.Record(stopwatch.Elapsed.TotalMilliseconds);
            return JsonSerializer.Deserialize<ShortUrl>(cached);
        }

        MetricsService.CacheMissesCounter.Add(1);
        
        // Query database
        var dbStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var url = await _innerRepository.GetByShortCodeAsync(shortCode);
        dbStopwatch.Stop();
        
        MetricsService.DatabaseLatency.Record(dbStopwatch.Elapsed.TotalMilliseconds);
        MetricsService.UrlResolutionLatency.Record(stopwatch.Elapsed.TotalMilliseconds);
        
        return url;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error");
        throw;
    }
}
```

---

### **6. APPLICATION INSIGHTS (AZURE)** (45 minutes)

**Install:** `dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore`

**Update Program.cs:**

```csharp
// Azure Application Insights
var appInsightsConnectionString = 
    builder.Configuration["ApplicationInsights:ConnectionString"];

if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(opt =>
        {
            opt.ConnectionString = appInsightsConnectionString;
        });
}
```

**Update appsettings.Production.json:**

```json
{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=xxx;IngestionEndpoint=https://xxx.applicationinsights.azure.com/;LiveEndpoint=https://xxx.livediagnostics.monitor.azure.com/"
  }
}
```

---

### **7. CI/CD PIPELINE - GITHUB ACTIONS** (45 minutes)

**File:** `.github/workflows/deploy.yml`

```yaml
name: Build & Deploy UrlShortener

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --configuration Release --no-restore
    
    - name: Run tests
      run: dotnet test --configuration Release --no-build --verbosity normal
    
    - name: Publish
      run: dotnet publish src/Services/UrlService/UrlService.csproj -c Release -o ${{ github.workspace }}/published
    
    - name: Build Docker image
      run: docker build -t urlshortener:${{ github.sha }} .
    
    - name: Login to Azure Container Registry
      uses: azure/docker-login@v1
      with:
        login-server: ${{ secrets.REGISTRY_LOGIN_SERVER }}
        username: ${{ secrets.REGISTRY_USERNAME }}
        password: ${{ secrets.REGISTRY_PASSWORD }}
    
    - name: Push to Azure Container Registry
      run: |
        docker tag urlshortener:${{ github.sha }} \
          ${{ secrets.REGISTRY_LOGIN_SERVER }}/urlshortener:latest
        docker push ${{ secrets.REGISTRY_LOGIN_SERVER }}/urlshortener:latest
    
    - name: Deploy to Azure App Service
      uses: azure/appservice-deploy@v1
      with:
        app-name: urlshortener-prod
        images: ${{ secrets.REGISTRY_LOGIN_SERVER }}/urlshortener:latest
        publish-profile: ${{ secrets.AZURE_PUBLISH_PROFILE }}
```

---

### **8. CUSTOM DASHBOARD - GRAFANA** (Optional, 1 hour)

**Add to docker-compose.yml:**

```yaml
prometheus:
  image: prom/prometheus:latest
  ports:
    - "9090:9090"
  volumes:
    - ./prometheus.yml:/etc/prometheus/prometheus.yml
  networks:
    - urlshortener-network

grafana:
  image: grafana/grafana:latest
  ports:
    - "3000:3000"
  environment:
    - GF_SECURITY_ADMIN_PASSWORD=admin
  networks:
    - urlshortener-network
```

**Access:** http://localhost:3000 (admin/admin)

---

## 📊 Monitoring Dashboard Recommendations

**Key Metrics to Monitor:**

```
AVAILABILITY:
├─ Uptime (%)
├─ Error rate (%)
└─ Response time (p50, p95, p99)

PERFORMANCE:
├─ Cache hit ratio (%)
├─ Database query latency (ms)
├─ URL resolution time (ms)
└─ Event publication latency (ms)

BUSINESS:
├─ URLs created/hour
├─ URLs accessed/hour
├─ Unique users/hour
└─ Click events/hour

INFRASTRUCTURE:
├─ CPU usage (%)
├─ Memory usage (%)
├─ Disk space (%)
└─ Network I/O (Mbps)
```

---

## 🚀 Deployment Strategies

### **Option 1: Azure App Service (RECOMMENDED FOR STARTUPS)**
✅ Easiest
✅ Auto-scaling
✅ Built-in monitoring
❌ Less control
❌ Higher cost

```bash
# Create App Service
az appservice plan create --resource-group mygroup --name myplan --sku B2
az webapp create --resource-group mygroup --plan myplan --name urlshortener
```

### **Option 2: Azure Container Instances**
✅ Simple
✅ Pay-per-second
✅ Great for stateless apps
❌ No orchestration
❌ Manual scaling

```bash
az container create --resource-group mygroup \
  --name urlshortener \
  --image myregistry.azurecr.io/urlshortener:latest
```

### **Option 3: Kubernetes (AKS)**
✅ Production-grade
✅ Auto-scaling
✅ Self-healing
❌ Complex
❌ Steep learning curve

```bash
az aks create --resource-group mygroup --name myaks --node-count 3
kubectl apply -f deployment.yaml
```

---

## 📋 Implementation Checklist

### Week 1 (Foundation)
- [ ] Add Health Check endpoint
- [ ] Create Dockerfile & docker-compose.yml
- [ ] Test locally with Docker Compose
- [ ] Configure Serilog to file

### Week 2 (Observability)
- [ ] Export logs to Seq
- [ ] Export traces to Jaeger
- [ ] Add custom metrics
- [ ] Create first dashboard

### Week 3 (Production)
- [ ] Set up GitHub Actions CI/CD
- [ ] Create Azure Container Registry
- [ ] Deploy to Azure App Service
- [ ] Configure alerts in Application Insights

### Week 4 (Operations)
- [ ] Create runbooks
- [ ] Set up on-call rotation
- [ ] Create custom Grafana dashboard
- [ ] Document troubleshooting procedures

---

## 🎯 Priority Pyramid (Do These First!)

```
┌───────────────────────────────────┐
│  OPTIONAL: Grafana Dashboards     │
├───────────────────────────────────┤
│  Custom Metrics + Alerts          │
├───────────────────────────────────┤
│  CI/CD Pipeline (GitHub Actions)  │
├───────────────────────────────────┤
│  Logging Aggregation (Seq)        │
├───────────────────────────────────┤
│  Trace Export (Jaeger)            │
├───────────────────────────────────┤
│  Docker Containerization          │
├───────────────────────────────────┤
│  Health Check Endpoint   ← START  │
└───────────────────────────────────┘
```

---

## 💡 Recommended Stack for YOUR Project

```
IMMEDIATE (This Month):
✅ Health Checks (/health endpoint)
✅ Docker + Docker Compose
✅ Serilog to file + console
✅ OpenTelemetry to Jaeger (local)

SOON (Next Month):
✅ Serilog export to Seq
✅ GitHub Actions CI/CD
✅ Azure Container Registry
✅ Azure App Service deployment

LATER (Optional):
✅ Application Insights APM
✅ Custom Grafana dashboards
✅ Kubernetes migration
✅ Advanced alerting
```

---

## 🔗 Quick Links

- [Health Checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [Docker Documentation](https://docs.docker.com/)
- [Serilog Sinks](https://github.com/serilog/serilog/wiki/Provided-Sinks)
- [Jaeger Documentation](https://www.jaegertracing.io/)
- [Seq Getting Started](https://docs.datalust.co/)
- [GitHub Actions](https://docs.github.com/en/actions)
- [Azure App Service](https://learn.microsoft.com/en-us/azure/app-service/)

