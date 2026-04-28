# UrlShortener Deployment Guide

## Scope

This guide covers the current working deployment slice of the repo:

- `ApiGateway`
- `IdentityService`
- `UrlService`
- SQL Server
- Redis
- RabbitMQ

`AnalyticsService` and `NotificationService` are not included in the first deployment path because they still need cleanup for container-safe service discovery.

## Current Gateway Shape

The cleaned gateway is intentionally small:

- public auth routes:
  - `POST /auth/register`
  - `POST /auth/login`
- public URL routes:
  - `GET /url/{shortCode}`
  - `POST /url/shorten`
  - `GET /url/my-urls`

The gateway validates JWTs for protected routes, but downstream services still validate the token again.

## Local Verification

Run the stack:

```powershell
docker compose -f D:\Projects\UrlShortener\docker-compose.yml up --build
```

Ports:

- `5000` -> `ApiGateway`
- `5001` -> `IdentityService`
- `5002` -> `UrlService`

Test through the gateway:

1. Register:

```powershell
$body = @{
    email = "test@example.com"
    fullName = "Test User"
    password = "Pass123$"
} | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "http://localhost:5000/auth/register" -ContentType "application/json" -Body $body
```

2. Login:

```powershell
$body = @{
    email = "test@example.com"
    password = "Pass123$"
} | ConvertTo-Json

$response = Invoke-RestMethod -Method Post -Uri "http://localhost:5000/auth/login" -ContentType "application/json" -Body $body
```

3. Shorten:

```powershell
$headers = @{ Authorization = "Bearer $($response.token)" }
$body = @{ originalUrl = "https://example.com" } | ConvertTo-Json

Invoke-RestMethod -Method Post -Uri "http://localhost:5000/url/shorten" -Headers $headers -ContentType "application/json" -Body $body
```

## Production Recommendation

Use this production topology:

- edge: `ALB` or `AWS API Gateway`
- runtime: `ECS Fargate`
- app containers:
  - `ApiGateway`
  - `IdentityService`
  - `UrlService`
- data services:
  - managed SQL Server
  - managed Redis
  - managed RabbitMQ or equivalent queue
- secrets:
  - `AWS Secrets Manager`
- logs and metrics:
  - `CloudWatch Logs`
  - OpenTelemetry-compatible sink

## Deployment Phases

### 1. Build Images

Build the three application images:

```powershell
docker build -f src/Gateways/ApiGateway/Dockerfile -t urlshortener-gateway .
docker build -f src/Services/IdentityService/Dockerfile -t urlshortener-identity .
docker build -f Dockerfile -t urlshortener-urlservice .
```

Push them to your registry.

### 2. Provision Infrastructure

Provision:

- VPC / subnets
- ECS cluster
- load balancer
- SQL Server
- Redis
- RabbitMQ
- secret store

### 3. Configure Runtime Secrets

At minimum:

#### ApiGateway

- `Jwt__Key`
- `Jwt__Issuer`
- `Jwt__Audiences__0`

#### IdentityService

- `ConnectionStrings__IdentityDB`
- `Jwt__Key`
- `Jwt__Issuer`
- `Jwt__Audiences__0`
- `Jwt__Audiences__1`
- `RabbitMQ__Host`
- `RabbitMQ__Port`
- `RabbitMQ__Username`
- `RabbitMQ__Password`

#### UrlService

- `ConnectionStrings__UrlDbString`
- `Jwt__Key`
- `Jwt__Issuer`
- `Jwt__Audiences__0`
- `UrlStorage__Mode`
- `UrlStorage__Redis__ConnectionString`
- `UrlStorage__SqlServer__ConnectionString`
- `RabbitMQ__Host`
- `RabbitMQ__Port`
- `RabbitMQ__Username`
- `RabbitMQ__Password`

## Suggested First Production Release

Deploy only:

- `ApiGateway`
- `IdentityService`
- `UrlService`

Leave out:

- `AnalyticsService`
- `NotificationService`
- extra dashboard services

This reduces the first production release to the path that is already validated locally:

- register
- login
- shorten
- redirect

## Next Cleanup After Deployment

After the first deployment works:

1. remove remaining local-development shortcuts
2. add health checks for `ApiGateway` and `IdentityService`
3. externalize JWT and DB secrets fully
4. clean `AnalyticsService` hardcoded `localhost`
5. clean `NotificationService` hardcoded `localhost`
