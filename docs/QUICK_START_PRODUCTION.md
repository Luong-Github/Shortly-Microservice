# UrlShortener: Quick Start - Production Ready (Local)

**Goal:** Run UrlShortener locally with all monitoring, logging, and health checks enabled. Takes ~30 minutes.

---

## Step 1: Create Dockerfile (2 minutes)

Create: `d:\Projects\UrlShortener\Dockerfile`

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

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "UrlService.dll"]
```

---

## Step 2: Create docker-compose.yml (3 minutes)

Create: `d:\Projects\UrlShortener\docker-compose.yml`

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
      - "5672:5672"      # AMQP port
      - "15672:15672"    # Management UI
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
      - "16686:16686"    # Jaeger UI
      - "6831:6831/udp"  # Jaeger Agent
      - "14268:14268"    # Jaeger Collector
    environment:
      - COLLECTOR_ZIPKIN_HOST_PORT=:9411
      - COLLECTOR_OTLP_ENABLED=true
    networks:
      - urlshortener
    profiles:
      - all

  # ===== OBSERVABILITY: LOGS =====
  seq:
    image: datalust/seq:latest
    container_name: urlshortener-logs
    ports:
      - "5341:5341"      # Seq API & UI
    environment:
      - ACCEPT_EULA=Y
    volumes:
      - seq_data:/data
    networks:
      - urlshortener
    profiles:
      - all

  # ===== OPTIONAL: METRICS & DASHBOARDS =====
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
      - GF_INSTALL_PLUGINS=grafana-piechart-panel
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

---

## Step 3: Update Program.cs - Add Health Checks & Logging Exports (5 minutes)

Replace the OpenTelemetry section in your **Program.cs**:

```csharp
// Configure Serilog with Seq export
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
    // NEW: Export to Seq (log aggregation)
    .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341")
    .CreateLogger();

// ... rest of Program.cs ...

// Add Health Checks (BEFORE health checks mapper)
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

// Configure OpenTelemetry with Jaeger export (SAME AS BEFORE BUT WITH JAEGER)
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
        // NEW: Export to Jaeger
        .AddJaegerExporter(opt =>
        {
            opt.AgentHost = builder.Configuration["Jaeger:AgentHost"] ?? "localhost";
            opt.AgentPort = int.Parse(
                builder.Configuration["Jaeger:AgentPort"] ?? "6831");
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());

// ... other middleware ...

// Map health check endpoints (ADD BEFORE app.Run())
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

---

## Step 4: Update appsettings.json (2 minutes)

Update `d:\Projects\UrlShortener\src\Services\UrlService\appsettings.json`:

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
    "Port": 5672,
    "Username": "guest",
    "Password": "guest"
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

---

## Step 5: Install Required NuGet Packages (2 minutes)

```powershell
cd d:\Projects\UrlShortener\src\Services\UrlService

# Add Jaeger exporter
dotnet add package OpenTelemetry.Exporter.Jaeger

# Add Seq sink
dotnet add package Serilog.Sinks.Seq

# Add HealthChecks
dotnet add package AspNetCore.HealthChecks.SqlServer
dotnet add package AspNetCore.HealthChecks.Redis
dotnet add package AspNetCore.HealthChecks.RabbitMQ
```

---

## Step 6: Update appsettings.Development.json (Optional, 1 minute)

Create `d:\Projects\UrlShortener\src\Services\UrlService\appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information"
    }
  },
  "Jaeger": {
    "AgentHost": "localhost",
    "AgentPort": 6831
  },
  "Seq": {
    "ServerUrl": "http://localhost:5341"
  }
}
```

---

## Step 7: Create Prometheus Config (1 minute)

Create: `d:\Projects\UrlShortener\prometheus.yml`

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'urlservice'
    static_configs:
      - targets: ['localhost:8080']
```

---

## 🚀 RUN IT ALL! (5 minutes)

### From PowerShell:

```powershell
cd d:\Projects\UrlShortener

# Build the project first
dotnet build src/Services/UrlService/UrlService.csproj

# Start all containers (infra + app)
docker-compose --profile all up --build

# (In another terminal, run migrations if needed)
# dotnet ef database update --project src/Services/UrlService
```

### It will start:
- **Your App**: http://localhost:8080
- **Jaeger UI**: http://localhost:16686
- **Seq Logs**: http://localhost:5341
- **RabbitMQ**: http://localhost:15672 (guest/guest)
- **Grafana**: http://localhost:3000 (admin/admin)
- **Prometheus**: http://localhost:9090
- **SQL Server**: localhost:1433
- **Redis**: localhost:6379

---

## 📊 Test Everything Works

### 1. Check Health Endpoint

```powershell
curl http://localhost:8080/health

# Output:
# {
#   "status": "Healthy",
#   "checks": {
#     "SqlServer": { "status": "Healthy" },
#     "Redis": { "status": "Healthy" },
#     "RabbitMQ": { "status": "Healthy" }
#   },
#   "totalDuration": "00:00:00.123456"
# }
```

### 2. Create a Short URL

```powershell
curl -X POST http://localhost:8080/api/urls/shorten `
  -ContentType "application/json" `
  -Body '{"longUrl":"https://www.example.com/very/long/path"}'

# Watch logs appear in:
# - Console (immediate)
# - logs/urlservice-YYYYMMDD.log (file)
# - http://localhost:5341 (Seq UI)
```

### 3. View Traces

```
Open: http://localhost:16686
Search for "UrlService"
Click on any trace
View: Spans, Latency, Tags
```

### 4. View Logs

```
Open: http://localhost:5341
See all structured logs in real-time
Filter by level, service, short code, user ID
```

### 5. Check RabbitMQ

```
Open: http://localhost:15672 (guest/guest)
View: click_events queue
See messages being published
```

---

## 🎯 What You Now Have

| Component | Purpose | Access |
|-----------|---------|--------|
| **UrlService** | Your app | http://localhost:8080 |
| **Health Checks** | System status | http://localhost:8080/health |
| **SQL Server** | Database | localhost:1433 |
| **Redis** | Cache | localhost:6379 |
| **RabbitMQ** | Message queue | http://localhost:15672 |
| **Jaeger** | Trace visualization | http://localhost:16686 |
| **Seq** | Log aggregation | http://localhost:5341 |
| **Prometheus** | Metrics collection | http://localhost:9090 |
| **Grafana** | Dashboards | http://localhost:3000 |

---

## 📈 Next: Add Custom Metrics (Optional, 15 minutes)

See **PRODUCTION_READINESS_GUIDE.md** → "Metrics & Custom Diagnostics" section

---

## 🐛 Troubleshooting

### Build Failed?
```powershell
dotnet clean
dotnet build
```

### Docker failed to start?
```powershell
docker-compose down -v
docker-compose --profile all up --build
```

### SQL Server connection failed?
```powershell
# Wait 30 seconds for SQL Server to initialize
docker exec urlshortener-db /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P SqlServer@123! -Q "SELECT 1"
```

### Redis connection failed?
```powershell
docker exec urlshortener-cache redis-cli ping
# Should return: PONG
```

### Can't see logs in Seq?
```powershell
# Check Seq is running
docker logs urlshortener-logs

# Check app is sending logs
curl http://localhost:8080/health
```

---

## ✅ Validation Checklist

- [ ] Docker-compose.yml created in project root
- [ ] Dockerfile created in project root
- [ ] Program.cs updated with Health Checks + Jaeger + Seq
- [ ] appsettings.json updated with connection strings
- [ ] NuGet packages installed (Jaeger, Seq, HealthChecks)
- [ ] `docker-compose --profile all up --build` runs successfully
- [ ] All 9 containers start without errors
- [ ] Health endpoint returns "Healthy": http://localhost:8080/health
- [ ] Logs visible in Seq: http://localhost:5341
- [ ] Traces visible in Jaeger: http://localhost:16686
- [ ] RabbitMQ management: http://localhost:15672

---

## TIME TO LIVE APP: 30 minutes ⏱️

You now have production-ready observability locally!
Next step: Deploy to Azure (see PRODUCTION_READINESS_GUIDE.md)

