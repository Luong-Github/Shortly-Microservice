# Authentication - Quick Reference

## Configure Auth Mode

**File:** `appsettings.Development.json` or `appsettings.json`

```json
{
  "Authentication": {
    "Mode": "IdentityServer"  // ✅ Development (default)
  }
}
```

**Options:**
- `IdentityServer` - Local OAuth2/OIDC server (development)
- `Cognito` - AWS-managed identity (production)
- `Hybrid` - Both providers (migration)

---

## Three Modes at a Glance

| Mode | Auth Provider | User Management | Service Auth | Best For |
|------|---------------|-----------------|--------------|----------|
| **IdentityServer** | Local (OAuth2) | ASP.NET Identity | Internal JWT | Development |
| **Cognito** | AWS | AWS Cognito | Internal JWT (optional) | Production |
| **Hybrid** | IdentityServer | ASP.NET Identity | Both providers | Migration |

---

## Getting Started

### 1. Run with IdentityServer (Default)
```bash
dotnet run
# ✓ Identity Service started with authentication mode: IdentityServer
```

### 2. Switch to Cognito Mode
Update `appsettings.json`:
```json
{
  "Authentication": {
    "Mode": "Cognito"
  },
  "Cognito": {
    "Authority": "https://cognito-idp.region.amazonaws.com/user-pool-id",
    "Audience": "client-id"
  }
}
```

### 3. Enable Hybrid Mode
```json
{
  "Authentication": {
    "Mode": "Hybrid"
  },
  "Cognito": {
    "Enabled": true,
    "Authority": "your-cognito-url",
    "Audience": "client-id"
  }
}
```

---

## Where Things Are

| Component | Location | Purpose |
|-----------|----------|---------|
| Auth modes | `Authentication/AuthenticationProviderConfig.cs` | Define IdentityServer/Cognito/Hybrid |
| IdentityServer setup | `Authentication/IdentityServerAuthProvider.cs` | OAuth2/OIDC configuration |
| Cognito setup | `Authentication/CognitoAuthProvider.cs` | AWS Cognito integration |
| JWT validation | `Authentication/JwtValidationProvider.cs` | Token validation |
| Fluent builder | `Authentication/AuthenticationBuilder.cs` | Configuration orchestration |
| Program setup | `Program.cs` (lines 35-48) | Mode selection and setup |

---

## Add JWT Token Validation

If you need to validate JWTs for service-to-service calls:

```csharp
var jwtKey = await secretsManager.GetSecretAsync("Jwt:Key");
var jwtIssuer = await secretsManager.GetSecretAsync("Jwt:Issuer");
var audiences = new[] { "your-api", "other-api" };

var jwtProvider = new JwtValidationProvider(services);
jwtProvider
    .AddJwtBearerAuthentication(jwtKey, jwtIssuer, audiences)
    .AddAuthorizationPolicies();
```

---

## Authorization Policies

All modes support standard policies:

```csharp
[Authorize(Policy = "AdminPolicy")]
public IActionResult AdminOnly() { }

[Authorize(Policy = "UserPolicy")]
public IActionResult UsersOnly() { }

// Any authenticated user
[Authorize]
public IActionResult AnyUser() { }
```

---

## Debugging

### Check Active Mode
```bash
# Look at console output when starting app
✓ Identity Service started with authentication mode: IdentityServer
```

### Check Configuration
```csharp
var mode = AuthenticationProviderConfig.GetActiveMode(configuration);
Console.WriteLine($"Mode: {mode}");
```

### Verify JWT Setup
```bash
# For local development, check these values exist:
# - Jwt:Key (min 32 chars)
# - Jwt:Issuer (valid URL)
# - Jwt:Audiences (array)
```

---

## Common Tasks

### Switch from IdentityServer to Cognito

**Step 1:** Create Cognito User Pool (AWS Console)
**Step 2:** Update config:
```json
{
  "Authentication": {
    "Mode": "Cognito"
  },
  "Cognito": {
    "Authority": "your-cognito-url",
    "Audience": "your-client-id"
  }
}
```
**Step 3:** Test and deploy

### Add Cognito Groups as Roles

Already configured! Cognito groups automatically mapped to roles:
```csharp
[Authorize(Roles = "Admins")]  // Maps to Cognito group "Admins"
public IActionResult AdminOnly() { }
```

### Use Multiple Auth Schemes (Advanced)

For complex scenarios with multiple token types:

```csharp
var provider = new JwtValidationProvider(services);
provider
    .AddJwtBearerScheme("internal", internalKey, internalIssuer, ["service-api"])
    .AddJwtBearerScheme("external", externalKey, externalIssuer, ["external-api"]);
```

Then in controllers:
```csharp
[Authorize(AuthenticationSchemes = "internal")]
public IActionResult InternalOnly() { }
```

---

## Production Checklist

- [ ] Set `Authentication:Mode` to `Cognito` or `Hybrid`
- [ ] Configure `Cognito:Authority` and `Cognito:Audience`
- [ ] Store JWT key in AWS Secrets Manager
- [ ] Set `Jwt:Key` to production key
- [ ] Verify IAM role can read secrets
- [ ] Test authentication flows
- [ ] Set up key rotation (optional)
- [ ] Enable HTTPS-only cookies
- [ ] Configure CORS if needed

---

## File Structure

```
IdentityService/
├── Program.cs                    # 65 lines: mode selection
├── appsettings.json             # Configuration
├── appsettings.Development.json # Dev values
├── Authentication/
│   ├── AuthenticationProviderConfig.cs   # Constants & modes
│   ├── IdentityServerAuthProvider.cs     # IdentityServer
│   ├── CognitoAuthProvider.cs            # Cognito
│   ├── JwtValidationProvider.cs          # JWT validation
│   └── AuthenticationBuilder.cs          # Builder pattern
├── AUTHENTICATION.md            # Complete guide
└── AUTH_REFACTORING_SUMMARY.md # What changed
```

---

## Related Docs

📖 **Full Documentation:** [AUTHENTICATION.md](./AUTHENTICATION.md)
🔐 **Secrets Setup:** [SECRETS_MANAGEMENT.md](../../SECRETS_MANAGEMENT.md)
✅ **Deployment:** [MIGRATION_CHECKLIST.md](../../MIGRATION_CHECKLIST.md)

---

## Need Help?

1. **Understanding the modes:** See [AUTHENTICATION.md](./AUTHENTICATION.md#mode-details)
2. **Configuration examples:** See [AUTHENTICATION.md](./AUTHENTICATION.md#configuration-examples)
3. **Troubleshooting:** See [AUTHENTICATION.md](./AUTHENTICATION.md#debugging--troubleshooting)
4. **What changed:** See [AUTH_REFACTORING_SUMMARY.md](./AUTH_REFACTORING_SUMMARY.md)
