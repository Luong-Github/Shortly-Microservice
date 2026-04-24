# Identity Service - Authentication Architecture

## Overview

The Identity Service now implements **clean separation of authentication provider concerns**. The architecture supports three authentication modes:

1. **IdentityServer** - Self-hosted OAuth2/OIDC provider (default)
2. **Cognito** - AWS-managed identity provider
3. **Hybrid** - Both IdentityServer and Cognito for different use cases

---

## Architecture

### Clean Provider Separation

Each authentication method is isolated in its own provider class:

```
Authentication/
├── AuthenticationProviderConfig.cs    # Configuration and mode selection
├── IdentityServerAuthProvider.cs      # IdentityServer (OAuth2/OIDC)
├── CognitoAuthProvider.cs             # AWS Cognito (SSO/Federation)
├── JwtValidationProvider.cs           # JWT token validation
└── AuthenticationBuilder.cs           # Fluent builder pattern
```

### File Descriptions

| File | Purpose | When to Use |
|------|---------|------------|
| **AuthenticationProviderConfig.cs** | Defines auth modes and shared constants | Configuration setup |
| **IdentityServerAuthProvider.cs** | IdentityServer OAuth2/OIDC setup | IdentityServer mode |
| **CognitoAuthProvider.cs** | AWS Cognito integration | Cognito or Hybrid mode |
| **JwtValidationProvider.cs** | JWT Bearer token validation | Service-to-service auth |
| **AuthenticationBuilder.cs** | Fluent builder for configuration | Program.cs setup |

---

## Mode Selection

### Configuration

Set the authentication mode in `appsettings.json`:

```json
{
  "Authentication": {
    "Mode": "IdentityServer"
  }
}
```

**Available modes:**
- `IdentityServer` - Local OAuth2/OIDC server (default, development-friendly)
- `Cognito` - AWS Cognito integration (production, AWS-native)
- `Hybrid` - Both providers for federation and SSO

### Debugging Mode

Check which mode is active in the console output:
```
✓ Identity Service started with authentication mode: IdentityServer
```

---

## Mode Details

### 1. IdentityServer Mode

**When to use:**
- Complete control over OAuth2/OIDC flows
- Development and testing environments
- Non-AWS deployments
- Need for local customization

**Configuration:**
```json
{
  "Authentication": {
    "Mode": "IdentityServer"
  },
  "Jwt": {
    "Key": "your-32-char-min-jwt-key",
    "Issuer": "http://localhost:5001",
    "Audiences": ["urlshortent_api", "analytics_api"]
  }
}
```

**What's configured:**
- ✅ ASP.NET Identity for user management
- ✅ IdentityServer4 as OAuth2/OIDC provider
- ✅ JWT Bearer validation for service-to-service calls
- ✅ Role-based authorization policies
- ✅ Developer signing credentials (development only)

**Program.cs:**
```csharp
var authBuilder = new AuthenticationBuilder(services, configuration, secretsManager);
await authBuilder.ConfigureIdentityServerModeAsync();
authBuilder.UseConfiguredMiddleware(app, AuthenticationMode.IdentityServer);
```

---

### 2. Cognito Mode

**When to use:**
- AWS-managed identity required
- Reduced operational overhead
- Enterprise SSO integration
- Multi-tenant SaaS deployments

**Configuration:**
```json
{
  "Authentication": {
    "Mode": "Cognito"
  },
  "Cognito": {
    "Authority": "https://cognito-idp.region.amazonaws.com/user-pool-id",
    "Audience": "client-id"
  },
  "Jwt": {
    "Key": "internal-service-jwt-key",
    "Issuer": "internal-issuer",
    "Audiences": ["urlshortent_api"]
  }
}
```

**What's configured:**
- ✅ AWS Cognito for user authentication
- ✅ JWT Bearer validation for Cognito tokens
- ✅ Optional internal service JWT validation
- ✅ Cognito group mapping to authorization roles
- ✅ Role-based policies

**Program.cs:**
```csharp
var authBuilder = new AuthenticationBuilder(services, configuration, secretsManager);
await authBuilder.ConfigureCognitoModeAsync(includeServiceJwt: true);
authBuilder.UseConfiguredMiddleware(app, AuthenticationMode.Cognito);
```

**Dual authentication:**
- **Primary:** Cognito tokens for user requests
- **Secondary:** Internal service JWTs for API-to-API communication

---

### 3. Hybrid Mode

**When to use:**
- IdentityServer for internal API service-to-service calls
- Cognito for external/user authentication
- Federated authentication with external identity providers
- Migrating from IdentityServer to Cognito

**Configuration:**
```json
{
  "Authentication": {
    "Mode": "Hybrid"
  },
  "Jwt": {
    "Key": "your-internal-service-jwt-key",
    "Issuer": "internal-issuer",
    "Audiences": ["internal-api"]
  },
  "Cognito": {
    "Enabled": true,
    "Authority": "https://cognito-idp.region.amazonaws.com/user-pool-id",
    "Audience": "cognito-client-id"
  }
}
```

**What's configured:**
- ✅ IdentityServer as primary service authenticator
- ✅ ASP.NET Identity for internal user management
- ✅ Optional Cognito for federated SSO
- ✅ JWT validation for both internal and Cognito tokens
- ✅ Unified authorization policies

---

## Authentication Flow Diagrams

### IdentityServer Mode

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │ 1. Login Request
       ▼
┌──────────────────┐
│  IdentityServer  │
│   (OAuth2/OIDC)  │
└──────┬───────────┘
       │ 2. User Authentication (ASP.NET Identity)
       │ 3. Issue JWT
       ▼
┌─────────────┐     ┌──────────────┐
│   (JWT)     │────▶│  Protected   │
└─────────────┘     │    API       │
                    └──────────────┘
                    4. Validate JWT
```

### Cognito Mode

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │ 1. Login Request
       ▼
┌──────────────────┐
│     Cognito      │
│   (AWS Managed)  │
└──────┬───────────┘
       │ 2. User Authentication
       │ 3. Issue JWT + ID Token
       ▼
┌─────────────┐     ┌──────────────┐
│   (JWT)     │────▶│  Protected   │
└─────────────┘     │    API       │
                    └──────────────┘
                    4. Validate via
                       Cognito Authority
```

### Hybrid Mode

```
┌─────────────────┐
│ User Client     │
└────────┬────────┘
         │
    ┌────┴────┐
    ▼         ▼
┌────────┐ ┌──────────┐
│Internal│ │ Cognito  │
│Service │ │  (SSO)   │
└────────┘ └──────────┘
    │         │
    └────┬────┘
         ▼
    Protected API
    1. Validate token
    2. Route based on issuer
```

---

## Provider Implementation Details

### IdentityServerAuthProvider

```csharp
var provider = new IdentityServerAuthProvider(services, configuration);
provider
    .AddAspNetIdentity<ApplicationUser, IdentityRole>()
    .AddIdentityServerProvider()
    .AddJwtBearerValidation(jwtKey, jwtIssuer, audiences)
    .AddAuthorizationPolicies();
```

**Responsibilities:**
- Configures ASP.NET Identity with EF Core stores
- Sets up IdentityServer as an OAuth2/OIDC server
- Adds JWT validation middleware
- Defines role-based authorization policies

### CognitoAuthProvider

```csharp
var provider = new CognitoAuthProvider(services, configuration);
provider
    .AddCognitoIdentity()
    .AddCognitoJwtValidation()
    .AddSecureServiceAuthentication(jwtKey, jwtIssuer, audiences)
    .AddCognitoAuthorizationPolicies();
```

**Responsibilities:**
- Integrates AWS Cognito client libraries
- Configures JWT validation for Cognito tokens
- Optionally adds internal service JWT validation
- Maps Cognito groups to authorization roles

### JwtValidationProvider

```csharp
var provider = new JwtValidationProvider(services);
provider
    .AddJwtBearerAuthentication(jwtKey, jwtIssuer, audiences)
    .AddAuthorizationPolicies();
```

**Responsibilities:**
- Standalone JWT token validation (no auth server)
- Support for multiple named schemes
- Scope-based and role-based policies

### AuthenticationBuilder

```csharp
var builder = new AuthenticationBuilder(services, config, secretsManager);
await builder.ConfigureIdentityServerModeAsync();
builder.UseConfiguredMiddleware(app, AuthenticationMode.IdentityServer);
```

**Responsibilities:**
- Selects correct provider based on configuration
- Loads JWT secrets from SecretsManager
- Validates configuration completeness
- Handles middleware ordering

---

## Program.cs Usage

### Before (Tangled Concerns)

```csharp
// Mix of IdentityServer + Cognito + JWT validation
builder.Services.AddCognitoIdentity();
builder.Services.AddIdentityServer(...)
    .AddAspNetIdentity<ApplicationUser>()
    .AddDeveloperSigningCredential();
    
builder.Services.AddAuthentication(...)
    .AddJwtBearer(options => { /* hardcoded config */ });

app.UseIdentityServer();
app.UseAuthorization();
```

**Problems:**
- Unclear which provider is active
- All setup code mixed together
- Hard to switch between modes
- Doesn't scale to 3+ plans

### After (Clean Separation)

```csharp
var authMode = AuthenticationProviderConfig.GetActiveMode(builder.Configuration);
var authBuilder = new AuthenticationBuilder(services, configuration, secretsManager);

switch (authMode)
{
    case AuthenticationMode.IdentityServer:
        await authBuilder.ConfigureIdentityServerModeAsync();
        break;
    case AuthenticationMode.Cognito:
        await authBuilder.ConfigureCognitoModeAsync(includeServiceJwt: true);
        break;
}

authBuilder.UseConfiguredMiddleware(app, authMode);
```

**Benefits:**
- Clear mode selection with logging
- Easy to understand which provider is active
- Simple to switch modes via configuration
- Each provider is self-contained
- Testable in isolation

---

## Configuration Examples

### Local Development (IdentityServer)

```json
{
  "Authentication": {
    "Mode": "IdentityServer"
  },
  "Jwt": {
    "Key": "this-is-a-development-key-min-32-chars-long",
    "Issuer": "http://localhost:5001",
    "Audiences": ["urlshortent_api", "analytics_api"]
  },
  "Cognito": {
    "Enabled": false
  }
}
```

### Staging (Cognito + Service Validation)

```json
{
  "Authentication": {
    "Mode": "Cognito"
  },
  "Cognito": {
    "Authority": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_xxxxx",
    "Audience": "staging-client-id"
  },
  "Jwt": {
    "Key": "your-internal-service-key",
    "Issuer": "https://staging.yourdomain.com",
    "Audiences": ["internal-api"]
  }
}
```

### Production (Hybrid for Migration)

```json
{
  "Authentication": {
    "Mode": "Hybrid"
  },
  "Jwt": {
    "Key": "${INTERNAL_JWT_KEY}",
    "Issuer": "https://identity.yourdomain.com",
    "Audiences": ["production-api"]
  },
  "Cognito": {
    "Enabled": true,
    "Authority": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_yyyyy",
    "Audience": "${COGNITO_CLIENT_ID}"
  }
}
```

---

## Security Considerations

### JWT Key Management

| Mode | Key Storage | Rotation |
|------|-------------|----------|
| IdentityServer | appsettings (dev) / AWS Secrets Manager (prod) | Annual |
| Cognito | AWS Cognito (managed) | Automatic |
| Hybrid | AWS Secrets Manager | Semi-annual |

### Best Practices

✅ **DO:**
- Use AWS Secrets Manager for JWT keys in production
- Rotate keys regularly (30-90 days recommended)
- Use HTTPS for all auth flows
- Validate token signatures
- Map Cognito groups to application roles
- Use separate keys for each environment

❌ **DON'T:**
- Store JWT keys in code or version control
- Use same key across environments
- Trust unverified tokens
- Expose JWK endpoints without rate limiting
- Store secrets in Docker images

---

## Debugging & Troubleshooting

### Mode Not Recognizing

**Issue:** Console shows wrong authentication mode

**Solution:**
1. Check `appsettings.json` for `Authentication:Mode`
2. Verify setting isn't overridden by environment variable
3. Check case sensitivity (exact match required)

### JWT Validation Fails

**Issue:** 401 Unauthorized on protected endpoints

**Solution:**
1. Verify JWT key matches between issuer and validator
2. Check token hasn't expired
3. Validate audience matches configured audience
4. For Cognito, verify Authority URL is correct

### Cognito Claims Missing

**Issue:** User roles/groups not available in claims

**Solution:**
1. Verify Cognito groups mapped to user (Cognito console)
2. Check token includes `cognito:groups` claim
3. Ensure `RoleClaimType = "cognito:groups"` configured

---

## Migration Guide

### Switching from IdentityServer to Cognito

1. **Create Cognito User Pool:**
   - Set up users and groups in AWS Cognito
   - Create app client
   - Note Authority and Client ID

2. **Update Configuration:**
   ```json
   {
     "Authentication": {
       "Mode": "Cognito"
     },
     "Cognito": {
       "Authority": "your-cognito-authority",
       "Audience": "your-client-id"
     }
   }
   ```

3. **Deploy to Staging:**
   - Test user login flows
   - Verify JWT validation
   - Check role-based access

4. **Gradual Rollout:**
   - Use Hybrid mode for overlap period
   - Monitor both providers
   - Gradually shift traffic to Cognito

---

## References

- [IdentityServer4 Documentation](https://identityserver4.readthedocs.io/)
- [AWS Cognito Documentation](https://docs.aws.amazon.com/cognito/)
- [JWT (RFC 7519)](https://tools.ietf.org/html/rfc7519)
- [OAuth 2.0 (RFC 6749)](https://tools.ietf.org/html/rfc6749)
- [OpenID Connect](https://openid.net/connect/)
