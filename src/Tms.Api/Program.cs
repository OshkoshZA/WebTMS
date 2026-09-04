using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Tms.Api.Auth;
using Tms.Api.Swagger;
using Tms.Infrastructure;
using Tms.Modules.Audit;
using Tms.Modules.Identity;
using Tms.Shared;

var builder = WebApplication.CreateBuilder(args);

// --- Tenant context (§4.1) — request-scoped, populated by TenantContextMiddleware,
// consumed by TmsDbContext's global query filters and the audit interceptor. ---
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<ICurrentUserAccessor>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<PendingPiiRedactionTracker>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<Tms.Api.Auth.JwtTokenService>();
builder.Services.AddScoped<Tms.Api.Auth.RefreshTokenService>();
builder.Services.AddScoped<Tms.Api.Services.CreditExposureService>();
builder.Services.AddScoped<Tms.Api.Services.LoadStatusService>();
builder.Services.AddScoped<Tms.Api.Services.DebriefApprovalService>();
builder.Services.AddScoped<Tms.Api.Services.ExceptionService>();

// --- Database (§4.1: EF Core global query filters are the application-layer half of
// tenant isolation; SQL Server Row-Level Security is the second, independent layer
// and is applied via a deployment script, not from this project). ---
builder.Services.AddDbContext<TmsDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});

// --- Field-level encryption for banking/PII (§12, §14.5) — the key ring persists to
// the same database as the data it protects (see TmsDbContext.DataProtectionKeys),
// so it survives restarts/redeploys and multiple instances the way a per-machine
// filesystem folder wouldn't. ---
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<TmsDbContext>()
    .SetApplicationName("Tms");

// --- Identity (§07) ---
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<TmsDbContext>()
    .AddSignInManager();

// --- Auth (§11.1): JWT bearer validates both the interactive-user login flow and
// the OAuth2 client-credentials grant — they produce the same kind of token, just
// via different issuance paths (AuthController), so one scheme covers both. ---
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSection["SigningKey"] ?? string.Empty)),
            ValidateLifetime = true
        };
    });

// Function-based authorization (§07): any function code works as a policy name
// without being registered by hand — see FunctionPolicyProvider.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, FunctionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, FunctionAuthorizationHandler>();
builder.Services.AddAuthorization();

// --- Rate limiting (§11.1: "per-client rate limits"). Partitioned by the caller's
// identity — client_id for an integration partner, the user id for an interactive
// session, remote IP for anyone not yet authenticated — and sized from the
// "rate_limit" claim JwtTokenService embeds at issuance, so no database lookup is
// needed per request to size the limit. ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)retryAfter.TotalSeconds
            : 60;
        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            $"{{\"error\":\"rate_limit_exceeded\",\"retryAfterSeconds\":{retryAfterSeconds}}}", ct);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var user = httpContext.User;
        var partitionKey = user.FindFirstValue("client_id")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        var permitLimit = int.TryParse(user.FindFirstValue("rate_limit"), out var claimed) ? claimed : 60;

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    // A tighter, IP-based policy for the unauthenticated auth endpoints (login,
    // token, refresh, §11.1) — exactly what brute-force/credential-stuffing
    // targets, so it gets its own limit regardless of the not-yet-established
    // caller identity the global limiter above would otherwise partition by.
    // Configurable (default 10/min) so Tms.Api.Tests, which runs every fixture and
    // race test's fresh logins from the same loopback IP, can raise it via
    // WithWebHostBuilder instead of the whole suite fighting production's limit.
    var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
    options.AddPolicy("auth", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// --- API versioning (§11): a URL-segment scheme, matching the /api/v1/... convention
// every route already used before this was wired up as a real mechanism rather than a
// hardcoded string. A new version ships as another [ApiVersion] attribute on a
// controller (or an action, for a per-endpoint bump) — existing v1 routes and clients
// are never touched. ---
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1.0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true; // echoes supported/deprecated versions in an api-supported-versions response header
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        foreach (var description in app.DescribeApiVersions())
        {
            options.SwaggerEndpoint(
                $"/swagger/{description.GroupName}/swagger.json",
                description.GroupName.ToUpperInvariant());
        }
    });

    // Demo Tenant/Company/admin user for local login — never runs outside Development.
    await Tms.Api.Seed.DevelopmentSeeder.SeedAsync(app.Services);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Baseline defense-in-depth response headers (no CORS policy: the SPAs — web/tms-app,
// tms-customer-portal, tms-supplier-portal — all proxy /api/* through their own dev
// server to this API rather than calling it cross-origin, per each one's vite.config.ts;
// the same same-origin-via-reverse-proxy shape is assumed in production, so the browser
// never actually makes a cross-origin request this API needs to answer for).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseAuthentication();
app.UseRateLimiter(); // needs the User principal from UseAuthentication to partition by client_id/user id
app.UseMiddleware<TenantContextMiddleware>(); // resolves TenantId/CompanyId before authorization runs
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level-statement Program class (implicitly internal) to
// Tms.Api.Tests' WebApplicationFactory<Program>, which needs a public type from
// this assembly to boot the app in-process.
public partial class Program { }
