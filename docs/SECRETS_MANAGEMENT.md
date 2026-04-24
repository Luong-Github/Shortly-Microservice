# Secrets Management Migration Guide

## Overview
All hard-coded secrets have been removed from the codebase. The project now uses **AWS Secrets Manager** for production and **appsettings.Development.json** for local development.

---

## What Changed

### 1. Removed Hard-Coded Secrets
❌ **Before:**
```csharp
ValidIssuer = "http://localhost:5001",
ValidAudience = "urlshortent_api",
IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("tQkM8cZXgXP1GK90841hBaoHIDoEwtud"))
```

✅ **After:**
```csharp
var secretsManager = new SecretsManager(builder.Configuration);
var jwtKey = await secretsManager.GetSecretAsync("Jwt:Key");
var jwtIssuer = await secretsManager.GetSecretAsync("Jwt:Issuer");
```

### 2. Configuration Updates
- Updated all `appsettings.json` files with configuration keys
- Created `SecretsManager` helper class for centralized secret retrieval
- Added AWS SDK to Infrastructure project

### 3. Services Updated
- ✅ ApiGateway
- ✅ UrlService
- ✅ IdentityService
- ✅ AnalyticsService
- ✅ NotificationService (stores secrets outside code)

---

## Local Development Setup

### For Windows / Local Development

1. **Update appsettings.Development.json** (Already done with safe defaults):
```json
{
  "Jwt": {
    "Key": "this-is-a-development-key-min-32-chars-long",
    "Issuer": "http://localhost:5001",
    "Audiences": ["http://localhost:5000", "http://localhost:5002"]
  }
}
```

2. **No additional setup needed** - Services will automatically use Development values when running locally.

---

## Production Setup (AWS)

### 1. Create Secrets in AWS Secrets Manager

Create the following secrets in AWS Secrets Manager (Console or CLI):

#### Via AWS Console:
1. Go to **AWS Secrets Manager** → **Store a new secret**
2. Choose **Other type of secret**
3. Create each secret with the key names below

#### Via AWS CLI:

```bash
# JWT Settings
aws secretsmanager create-secret \
  --name "Jwt:Key" \
  --secret-string "your-production-jwt-key-at-least-32-characters"

aws secretsmanager create-secret \
  --name "Jwt:Issuer" \
  --secret-string "https://your-production-domain.com"

aws secretsmanager create-secret \
  --name "Jwt:Audiences:0" \
  --secret-string "your-api-audience"

aws secretsmanager create-secret \
  --name "Jwt:Audiences:1" \
  --secret-string "your-analytics-audience"

# Database Connection Strings (if not using RDS IAM auth)
aws secretsmanager create-secret \
  --name "ConnectionStrings:UrlDbString" \
  --secret-string "Server=your-db-server;Database=UrlDb;..."

# Redis Connection String
aws secretsmanager create-secret \
  --name "ConnectionStrings:Redis" \
  --secret-string "your-redis-endpoint:6379"
```

### 2. Grant IAM Permissions to ECS Tasks

Add the following policy to your ECS task execution role:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue"
      ],
      "Resource": [
        "arn:aws:secretsmanager:region:account-id:secret:Jwt:*",
        "arn:aws:secretsmanager:region:account-id:secret:ConnectionStrings:*"
      ]
    }
  ]
}
```

### 3. Rotate Secrets Regularly

AWS Secrets Manager supports automatic rotation. Set up rotation:

```bash
aws secretsmanager rotate-secret \
  --secret-id "Jwt:Key" \
  --rotation-rules AutomaticallyAfterDays=30
```

---

## Secret Naming Convention

All secrets follow a hierarchical naming pattern matching appsettings.json:

```
Jwt:Key                          # JWT signing key
Jwt:Issuer                       # JWT issuer URL
Jwt:Audiences:0                  # First audience
Jwt:Audiences:1                  # Second audience
ConnectionStrings:UrlDbString    # URL Service DB connection
ConnectionStrings:Redis          # Redis connection string
ConnectionStrings:AppAnalyticsDb # Analytics DB connection
```

---

## SecretsManager Usage

### Basic Usage

```csharp
var secretsManager = new SecretsManager(builder.Configuration);

// Get a simple string secret
var jwtKey = await secretsManager.GetSecretAsync("Jwt:Key");

// Get with default fallback
var audience = await secretsManager.GetSecretAsync("Jwt:Audience", "default_audience");

// Get JSON secret (for complex configurations)
var dbConfig = await secretsManager.GetJsonSecretAsync<DbConfiguration>("DatabaseConfig");
```

### How It Works

1. **Production (AWS Credentials Available)**:
   - First checks **AWS Secrets Manager**
   - Falls back to **appsettings configuration** if not found in AWS
   - Caches results

2. **Development (No AWS Credentials)**:
   - Reads from **User Secrets** or **appsettings.Development.json**
   - Gracefully handles missing AWS credentials

3. **Caching**:
   - Retrieved secrets are cached in memory to reduce API calls
   - Cache is per-secret, survives app lifetime

---

## Deployment Checklist

- [ ] All secrets created in AWS Secrets Manager
- [ ] ECS task execution role has `secretsmanager:GetSecretValue` permission
- [ ] Terraform infrastructure updated with IAM policies
- [ ] Secrets validated in staging environment before production
- [ ] Rotation schedule set up (optional but recommended)
- [ ] Monitoring/alerts configured for failed secret retrieval
- [ ] Team members have read access to AWS Secrets Manager (if needed for debugging)

---

## Troubleshooting

### Secret Not Found Error

**Error**: `InvalidOperationException: JWT configuration (Key, Issuer) is missing...`

**Solution**:
1. Verify secret exists in AWS Secrets Manager: `aws secretsmanager get-secret-value --secret-id "Jwt:Key"`
2. Check IAM role permissions for the ECS task
3. Verify region matches deployment region
4. For local dev, check `appsettings.Development.json` has the required keys

### AWS Credentials Missing

**Warning**: `Failed to retrieve secret from AWS: ...`

**Solution** (Normal for local development):
- This is expected when running locally without AWS credentials
- Service will fall back to appsettings configuration
- Add credentials to AWS CLI for production deployments

### Slow Secret Retrieval

**Solution**:
- Secrets are cached after first retrieval - subsequent calls are instant
- If still slow, check AWS Secrets Manager API throttling in CloudWatch
- Consider batch retrieval or pre-loading secrets during startup

---

## Security Best Practices

✅ **DO:**
- Store all secrets in AWS Secrets Manager for production
- Use separate secrets for each environment (Dev, Staging, Prod)
- Rotate JWT keys regularly (every 30-90 days recommended)
- Use IAM roles instead of access keys for ECS tasks
- Restrict secret access to specific IAM roles
- Monitor and log all secret access in CloudTrail

❌ **DON'T:**
- Store secrets in environment variables in production
- Commit secrets to version control (even in .gitignore files)
- Share AWS Secrets Manager access widely
- Use the same JWT key across environments
- Store secrets longer than necessary

---

## Next Steps

1. **Create AWS Secrets** using the CLI commands above
2. **Update Terraform** to include IAM permissions
3. **Test locally** with appsettings.Development.json
4. **Deploy to staging** and verify secret retrieval
5. **Monitor** CloudWatch logs for any secret-related errors

---

## Reference

- [AWS Secrets Manager Documentation](https://docs.aws.amazon.com/secretsmanager/)
- [SecretsManager.cs Source](../Infrastructure/Infrastructure/Services/SecretsManager.cs)
