using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Tms.Api.Auth;
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
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<Tms.Api.Auth.JwtTokenService>();
builder.Services.AddScoped<Tms.Api.Auth.RefreshTokenService>();
builder.Services.AddScoped<Tms.Api.Services.CreditExposureService>();

// --- Database (§4.1: EF Core global query filters are the application-layer half of
// tenant isolation; SQL Server Row-Level Security is the second, independent layer
// and is applied via a deployment script, not from this project). ---
builder.Services.AddDbContext<TmsDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"));
    options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});

// --- Identity (§07) ---
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<TmsDbContext>()
    .AddSignInManager();

// --- Auth (§11.1): JWT bearer for interactive users and portal contacts; OAuth2
// client-credentials for system-to-system integration partners is added alongside
// this in a later phase once Tms.Modules.Integration is built out. ---
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Demo Tenant/Company/admin user for local login — never runs outside Development.
    await Tms.Api.Seed.DevelopmentSeeder.SeedAsync(app.Services);
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>(); // resolves TenantId/CompanyId before authorization runs
app.UseAuthorization();

app.MapControllers();

app.Run();
