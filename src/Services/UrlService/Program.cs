using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Reflection;
using System.Text;
using Infrastructure.Services;
using UrlService.Data;
using UrlService.Repositories;
using UrlService.Services;
using UrlService.Storage;
using UrlService.Events;
using Amazon.Runtime;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using UrlService.Jobs;
using Serilog;
using Serilog.Events;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for structured logging
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

// Use Serilog for logging
builder.Host.UseSerilog();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

builder.Services.AddDbContextFactory<UrlDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("UrlDbString"));
});
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<UrlDbContext>>().CreateDbContext());

// Configure multi-tier URL storage (Development, Production, Enterprise, Archive)
var storageMode = builder.Configuration.GetValue<string>("UrlStorage:Mode") ?? "Development";
builder.Services.AddUrlStorage(builder.Configuration, storageMode);

builder.Services.AddScoped<UrlShorteningService>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
});

// Register RabbitMQ event publisher as singleton (connection pooled)
builder.Services.AddSingleton<IClickEventPublisher, ClickEventPublisher>();

builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    var cleanupJobKey = new JobKey("ExpiredUrlCleanupJob");
    q.AddJob<ExpiredUrlCleanupJob>(opts => opts.WithIdentity(cleanupJobKey));
    q.AddTrigger(opts => opts
        .ForJob(cleanupJobKey)
        .WithIdentity("ExpiredUrlCleanupTrigger")
        .WithCronSchedule("0 0 * * * ?")); // Runs every midnight
});

// Register SecretsManager
var secretsManager = new SecretsManager(builder.Configuration);
builder.Services.AddSingleton(secretsManager);

// Load JWT configuration from secrets
var jwtKey = await secretsManager.GetSecretAsync("Jwt:Key");
var jwtIssuer = await secretsManager.GetSecretAsync("Jwt:Issuer");
var jwtAudience = await secretsManager.GetSecretAsync("Jwt:Audiences:0");

if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer))
{
    throw new InvalidOperationException("JWT configuration (Key, Issuer) is missing. Configure via AWS Secrets Manager or appsettings.Development.json");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience ?? "urlshortent_api",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("UrlDbString") ?? "Server=sqlserver;Database=UrlShortener",
        name: "SqlServer",
        tags: new[] { "ready" })
    .AddRedis(
        builder.Configuration["Cache:RedisConnection"] ?? "redis:6379",
        name: "Redis",
        tags: new[] { "ready" })
    .AddRabbitMQ(
        new Uri($"amqp://guest:guest@{builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq"}:{builder.Configuration["RabbitMQ:Port"] ?? "5672"}"),
        name: "RabbitMQ",
        tags: new[] { "ready" });

var app = builder.Build();

// Ensure database exists before attempting migrations  
try
{
    Console.WriteLine("[INIT] Starting database initialization...");
    Log.Information("Starting database initialization...");
    
    var connectionString = builder.Configuration.GetConnectionString("UrlDbString");
    if (!string.IsNullOrEmpty(connectionString))
    {
        Console.WriteLine("[INIT] Connection string found");
        Log.Information("Attempting to create database if not exists...");
        
        // Parse connection string
        var masterConnBuilder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = masterConnBuilder.InitialCatalog;
        masterConnBuilder.InitialCatalog = "master";
        
        Console.WriteLine($"[INIT] Creating database '{databaseName}' on server '{masterConnBuilder.DataSource}'...");
        Log.Information($"Creating database '{databaseName}' on server '{masterConnBuilder.DataSource}'...");
        
        // Connect to master database and create target database
        using (var masterConnection = new SqlConnection(masterConnBuilder.ConnectionString))
        {
            Console.WriteLine("[INIT] Opening connection to master database...");
            await masterConnection.OpenAsync();
            Console.WriteLine("[INIT] Connection opened, creating database...");
            
            using (var cmd = masterConnection.CreateCommand())
            {
                cmd.CommandText = $"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = N'{databaseName}') BEGIN CREATE DATABASE [{databaseName}]; END";
                await cmd.ExecuteNonQueryAsync();
            }
            Console.WriteLine($"[INIT] ✓ Database '{databaseName}' is ready");
            Log.Information($"✓ Database '{databaseName}' is ready");
        }
    }
    
    // Now create/migrate tables
    Console.WriteLine("[INIT] Creating tables if they don't exist...");
    Log.Information("Creating tables if they don't exist...");
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<UrlDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }
    
    Console.WriteLine("[INIT] ✓ Database schema initialized successfully");
    Log.Information("✓ Database schema initialized successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"[INIT ERROR] {ex}");
    Log.Error(ex, "✗ Error initializing database - continuing anyway");
}
Console.WriteLine("[INIT] Database initialization block completed");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = reg => reg.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = reg => reg.Tags.Contains("ready")
});

app.MapControllers();


app.UseHttpsRedirection();

app.Run();
