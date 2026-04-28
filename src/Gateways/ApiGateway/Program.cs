using ApiGateway.Middlewares;
using AspNetCoreRateLimit;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();

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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Bearer",
        options =>
         {
             options.RequireHttpsMetadata = false;
             options.SaveToken = true;
             options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
             {
                 ValidateIssuer = true,
                 ValidateAudience = true,
                 ValidateLifetime = true,
                 ValidateIssuerSigningKey = true,
                 ValidIssuer = jwtIssuer,
                 ValidAudience = jwtAudience ?? "urlshortent_api",
                 IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
             };
         }
    );


builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddAuthorization();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<RateLimitLoggingMiddleware>();
app.UseIpRateLimiting();

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpsRedirection();
await app.UseOcelot();

app.Run();
