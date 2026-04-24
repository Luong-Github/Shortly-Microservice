using AspNetCoreRateLimit;
using IdentityService.Configs;
using IdentityService.Data;
using IdentityService.Middleware;
using IdentityService.Models;
using IdentityService.Services;
using IdentityService.Authentication;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure database
builder.Services.AddDbContext<AppIdentityDbContext>(
    options => options.UseSqlServer(builder.Configuration.GetConnectionString("IdentityDB"))
);

// Register infrastructure services
var secretsManager = new SecretsManager(builder.Configuration);
builder.Services.AddSingleton(secretsManager);

// Register business services
builder.Services.AddScoped<JwtTokenGenerator>();
builder.Services.AddScoped<IAffiliateService, AffiliateService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<IReferralService, ReferralService>();

// Register MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

// Configure rate limiting
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "*",
            Limit = 1000,
            Period = "1d"
        }
    };
});
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();

// Configure authentication based on configured mode
var authMode = AuthenticationProviderConfig.GetActiveMode(builder.Configuration);
var authBuilder = new AuthenticationBuilder(builder.Services, builder.Configuration, secretsManager);

switch (authMode)
{
    case AuthenticationMode.IdentityServer:
        await authBuilder.ConfigureIdentityServerModeAsync();
        break;

    case AuthenticationMode.Cognito:
        await authBuilder.ConfigureCognitoModeAsync(includeServiceJwt: true);
        break;

    case AuthenticationMode.Hybrid:
        await authBuilder.ConfigureHybridModeAsync();
        break;

    default:
        throw new InvalidOperationException($"Unknown authentication mode: {authMode}");
}

// Add controllers and API docs
builder.Services.AddControllers();

var app = builder.Build();

// Configure HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Apply authentication/authorization middleware based on mode
authBuilder.UseConfiguredMiddleware(app, authMode);

// Apply custom middleware
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<SubscriptionMiddleware>();
app.UseIpRateLimiting();

// Map endpoints
app.MapControllers();

// HTTPS redirect
app.UseHttpsRedirection();

// Log configured auth mode
Console.WriteLine($"✓ Identity Service started with authentication mode: {authMode}");

app.Run();
