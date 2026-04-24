# 30-Minute Quick Setup: File-by-File Checklist

**Goal:** Get UrlShortener running locally with full production observability (logs, traces, health checks, dashboards)

**Time:** 30 minutes total | **Files to Create:** 2 | **Code Changes:** 1 file (Program.cs)

---

## 📋 COMPLETE CHECKLIST

### **[1/3] CREATE: Dockerfile** (2 minutes)

📍 **Location:** `d:\Projects\UrlShortener\Dockerfile` (project root)

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /src

COPY ["src/Services/UrlService/UrlService.csproj", "src/Services/UrlService/"]
COPY ["src/Shared/Shared.Domain/Shared.Domain.csproj", "src/Shared/Shared.Domain/"]
COPY ["src/Shared/Shared/Shared.csproj", "src/Shared/Shared/"]
COPY ["src/Infrastructure/Infrastructure/Infrastructure.csproj", "src/Infrastructure/Infrastructure/"]

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

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "UrlService.dll"]
```

✅ **Status:** Created
**Verify:** `docker build . -t urlshortener` (don't run yet)

---

### **[2/3] CREATE: docker-compose.yml** (3 minutes)

📍 **Location:** `d:\Projects\UrlShortener\docker-compose.yml` (project root)

```yaml
version: '3.8'

services:
  # ===== YOUR APP =====
  urlservice:
    build: .
    container_name: urlshortener-app
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__UrlDbString=Server=sqlserver;Initial Catalog=UrlShortener;User Id=sa;Password=SqlServer@123!;Encrypt=false;TrustServerCertificate=true
      - Cache__RedisConnection=redis:6379
      - RabbitMQ__Host=rabbitmq
      - Jaeger__AgentHost=jaeger
      - Jaeger__AgentPort=6831
      - Seq__ServerUrl=http://seq:5341
    depends_on:
      - sqlserver
      - redis
      - rabbitmq
      - jaeger
      - seq
    networks:
      - urlshortener
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 10s
    profiles:
      - all

  # ===== DATABASES =====
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: urlshortener-db
    ports:
      - "1433:1433"
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=SqlServer@123!
      - MSSQL_PID=Developer
    volumes:
      - sqldata:/var/opt/mssql
    networks:
      - urlshortener
    healthcheck:
      test: ["CMD", "/opt/mssql-tools/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", "SqlServer@123!", "-Q", "SELECT 1"]
      interval: 10s
      timeout: 5s
      retries: 3
    profiles:
      - all

  redis:
    image: redis:7-alpine
    container_name: urlshortener-cache
    ports:
      - "6379:6379"
    networks:
      - urlshortener
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 3
    profiles:
      - all

  # ===== MESSAGE QUEUE =====
  rabbitmq:
    image: rabbitmq:3.12-management-alpine
    container_name: urlshortener-mq
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      - RABBITMQ_DEFAULT_USER=guest
      - RABBITMQ_DEFAULT_PASS=guest
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    networks:
      - urlshortener
    healthcheck:
      test: rabbitmq-diagnostics -q ping
      interval: 10s
      timeout: 5s
      retries: 3
    profiles:
      - all

  # ===== OBSERVABILITY: TRACING =====
  jaeger:
    image: jaegertracing/all-in-one:latest
    container_name: urlshortener-tracing
    ports:
      - "16686:16686"
      - "6831:6831/udp"
      - "14268:14268"
    networks:
      - urlshortener
    profiles:
      - all

  # ===== OBSERVABILITY: LOGS =====
  seq:
    image: datalust/seq:latest
    container_name: urlshortener-logs
    ports:
      - "5341:5341"
    environment:
      - ACCEPT_EULA=Y
    volumes:
      - seq_data:/data
    networks:
      - urlshortener
    profiles:
      - all

  # ===== OPTIONAL: METRICS =====
  prometheus:
    image: prom/prometheus:latest
    container_name: urlshortener-metrics
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus_data:/prometheus
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
    networks:
      - urlshortener
    profiles:
      - all

  grafana:
    image: grafana/grafana:latest
    container_name: urlshortener-dashboard
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
    volumes:
      - grafana_data:/var/lib/grafana
    networks:
      - urlshortener
    depends_on:
      - prometheus
    profiles:
      - all

volumes:
  sqldata:
  rabbitmq_data:
  seq_data:
  prometheus_data:
  grafana_data:

networks:
  urlshortener:
    driver: bridge
```

✅ **Status:** Created
**Verify:** `docker-compose config` (should output YAML without errors)

---

### **[3/3] UPDATE: Program.cs** (10 minutes)

📍 **Location:** `d:\Projects\UrlShortener\src\Services\UrlService\Program.cs`

**Find section:** Look for `// Configure Serilog for structured logging`

**BEFORE YOUR CHANGES, Show the old code:**
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File("logs/urlservice-.log", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();
```

**REPLACE WITH:**
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .Enrich.WithProperty("Application", "UrlService")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File("logs/urlservice-.log", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341")
    .CreateLogger();
```

**Now find:** `// Configure OpenTelemetry for distributed tracing`

**BEFORE YOUR CHANGES, show old:**
```csharp
// Configure OpenTelemetry for distributed tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("UrlService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("UrlService.Operations")           // Custom spans
        .AddSource("UrlService.Repositories.*")       // Repository spans
        .AddSource("UrlService.Events.*")             // Event spans
        .AddSource("UrlService.Services.*")           // Service spans
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());
```

**REPLACE WITH:**
```csharp
// Configure OpenTelemetry for distributed tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("UrlService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("UrlService.Operations")
        .AddSource("UrlService.Repositories.*")
        .AddSource("UrlService.Events.*")
        .AddSource("UrlService.Services.*")
        .AddConsoleExporter()
        .AddJaegerExporter(opt =>
        {
            opt.AgentHost = builder.Configuration["Jaeger:AgentHost"] ?? "localhost";
            opt.AgentPort = int.Parse(builder.Configuration["Jaeger:AgentPort"] ?? "6831");
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());
```

**Now find:** `var app = builder.Build();` (before this line)

**ADD THIS NEW SECTION:**
```csharp
// Add Health Checks (BEFORE app = builder.Build())
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("UrlDbString") ?? "Server=.;Database=UrlShortener",
        name: "SqlServer",
        tags: new[] { "ready" })
    .AddRedis(
        builder.Configuration["Cache:RedisConnection"] ?? "localhost:6379",
        name: "Redis",
        tags: new[] { "ready" })
    .AddRabbitMQ(
        new Uri("amqp://localhost:5672"),
        name: "RabbitMQ",
        tags: new[] { "ready" });
```

**Now find:** `app.MapControllers();`

**ADD BEFORE IT:**
```csharp
// Map health check endpoints
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

✅ **Status:** Updated

---

### **[4/4] INSTALL: NuGet Packages** (3 minutes)

📍 **Location:** Terminal

```powershell
cd d:\Projects\UrlShortener\src\Services\UrlService

# Install Jaeger exporter
dotnet add package OpenTelemetry.Exporter.Jaeger

# Install Seq logging sink
dotnet add package Serilog.Sinks.Seq

# Install Health Checks
dotnet add package AspNetCore.HealthChecks.SqlServer
dotnet add package AspNetCore.HealthChecks.Redis
dotnet add package AspNetCore.HealthChecks.RabbitMQ
```

✅ **Status:** Installed

---

### **[5/5] UPDATE: appsettings.json** (2 minutes)

📍 **Location:** `d:\Projects\UrlShortener\src\Services\UrlService\appsettings.json`

**UPDATE TO:**
```json
{
  "ConnectionStrings": {
    "UrlDbString": "Server=localhost;Database=UrlShortener;User Id=sa;Password=SqlServer@123!;Encrypt=false;TrustServerCertificate=true;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },
  "Cache": {
    "RedisConnection": "localhost:6379"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672
  },
  "Jaeger": {
    "AgentHost": "localhost",
    "AgentPort": 6831
  },
  "Seq": {
    "ServerUrl": "http://localhost:5341"
  },
  "UrlStorage": {
    "Mode": "Production"
  }
}
```

✅ **Status:** Updated

---

### **[6/6] CREATE: prometheus.yml** (1 minute)

📍 **Location:** `d:\Projects\UrlShortener\prometheus.yml` (project root)

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'urlservice'
    static_configs:
      - targets: ['localhost:8080']
```

✅ **Status:** Created

---

## 🚀 BUILD & RUN (5 minutes)

### **Step 1: Build the .NET Project**
```powershell
cd d:\Projects\UrlShortener\src\Services\UrlService
dotnet build
```

✅ Expected: `Build succeeded` (no errors)

### **Step 2: Start All Containers**
```powershell
cd d:\Projects\UrlShortener
docker-compose --profile all up --build
```

✅ Expected output:
```
urlshortener-app      | info: Application started
urlshortener-tracing  | Started Jaeger
urlshortener-logs     | Seq is running
urlshortener-metrics  | Prometheus server started
urlshortener-dashboard | Grafana started
```

**Wait 10-15 seconds for SQL Server to initialize**

---

## ✅ VERIFICATION (5 minutes)

### 1️⃣ Health Check
```powershell
curl http://localhost:8080/health
```
✅ Expected: JSON response showing "Healthy" for SqlServer, Redis, RabbitMQ

### 2️⃣ Create a Short URL
```powershell
curl -X POST http://localhost:8080/api/urls/shorten `
  -ContentType "application/json" `
  -Body '{"longUrl":"https://www.example.com/very/long/path"}'
```
✅ Expected: Returns `{"shortCode":"abc123","id":"..."}` or similar

### 3️⃣ View Logs in Seq
```
Open: http://localhost:5341
✅ Expected: See your request logs with timestamps and properties
```

### 4️⃣ View Traces in Jaeger
```
Open: http://localhost:16686
Search: "UrlService" or look for any trace
✅ Expected: See distributed traces with spans
```

### 5️⃣ View Dashboards in Grafana
```
Open: http://localhost:3000 (admin/admin)
✅ Expected: See Grafana login, can connect Prometheus data source
```

### 6️⃣ Check RabbitMQ
```
Open: http://localhost:15672 (guest/guest)
✅ Expected: See click_events queue with messages
```

---

## 📊 QUICK URL REFERENCE

| Service | URL | Purpose |
|---------|-----|---------|
| **UrlShortener API** | http://localhost:8080 | Your app |
| **Health Endpoint** | http://localhost:8080/health | System status |
| **Seq Logs** | http://localhost:5341 | Log aggregation |
| **Jaeger Traces** | http://localhost:16686 | Trace visualization |
| **Grafana Dashboard** | http://localhost:3000 | Metrics (admin/admin) |
| **Prometheus Metrics** | http://localhost:9090 | Raw metrics |
| **RabbitMQ Management** | http://localhost:15672 | Message queue (guest/guest) |
| **SQL Server** | localhost:1433 | Database |
| **Redis** | localhost:6379 | Cache |

---

## 🎯 YOUR NEXT STEPS

### NOW (30 min) ✅
- [ ] Create: Dockerfile
- [ ] Create: docker-compose.yml
- [ ] Create: prometheus.yml
- [ ] Update: Program.cs
- [ ] Update: appsettings.json
- [ ] Install: NuGet packages
- [ ] Run: `docker-compose --profile all up --build`
- [ ] Verify: All health checks pass

### TOMORROW (Network)
- [ ] Add tracing to ClickEventPublisher (see OBSERVABILITY_GUIDE.md)
- [ ] Add tracing to CachedUrlRepository
- [ ] Add logging to Controllers
- [ ] Make production request and trace it end-to-end

### THIS WEEK (Production)
- [ ] Set up Azure resources
- [ ] Deploy to Azure App Service
- [ ] Configure Application Insights
- [ ] Create CI/CD pipeline with GitHub Actions

---

## 🐛 Quick Troubleshooting

| Problem | Solution |
|---------|----------|
| **Build fails** | `dotnet clean && dotnet build` |
| **Docker error** | `docker-compose down -v && docker-compose --profile all up --build` |
| **SQL Server timeout** | Wait 30 seconds, containers are still starting |
| **Can't reach health endpoint** | Check: `docker ps` to see if containers running |
| **No logs in Seq** | Verify: `docker logs urlshortener-app` to see app logs |
| **Jaeger empty** | Make an API request first: `curl http://localhost:8080/api/urls/shorten ...` |

---

## ✨ YOU'RE DONE!

You now have:
- ✅ Health checks on `/health`
- ✅ Structured logs in Seq UI
- ✅ Distributed traces in Jaeger
- ✅ Metrics in Prometheus/Grafana
- ✅ Docker containerization ready
- ✅ Full production setup locally

**NEXT:** Read [PRODUCTION_READINESS_GUIDE.md](PRODUCTION_READINESS_GUIDE.md) to deploy to Azure!

