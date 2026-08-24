using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Audit;
using Tms.Modules.Fleet;
using Tms.Modules.Identity;
using Tms.Modules.Loads;
using Tms.Modules.Privacy;
using Tms.Modules.Rating;
using Tms.Shared;

namespace Tms.Infrastructure;

/// <summary>
/// The one DbContext spanning every module (docs/architecture.html §03 — "modular
/// monolith": separate modules, one shared database). Tenant/company isolation
/// (§4.1) is enforced here via global query filters referencing <see cref="CurrentTenantId"/>
/// and <see cref="CurrentCompanyId"/> — properties EF Core re-evaluates against
/// whichever DbContext instance is actually running a given query, so a filter
/// defined once at model-build time still reflects the live request's tenant.
/// </summary>
public class TmsDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantContext _tenantContext;

    public TmsDbContext(DbContextOptions<TmsDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // Exposed for the global query filters below — re-read from the injected,
    // request-scoped ITenantContext every time this DbContext instance is asked.
    public Guid? CurrentTenantId => _tenantContext.TenantId;
    public Guid? CurrentCompanyId => _tenantContext.CompanyId;
    public bool IsPlatformSupportBypass => _tenantContext.IsPlatformSupport;

    // Reference data (shared across every Tenant — §04)
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<LoadType> LoadTypes => Set<LoadType>();
    public DbSet<Function> Functions => Set<Function>();

    // Identity (§04, §07)
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<RoleFunction> RoleFunctions => Set<RoleFunction>();
    public DbSet<UserCompanyRole> UserCompanyRoles => Set<UserCompanyRole>();

    // Fleet (§5.1)
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();

    // Loads (§5.1, §5.2, §5.5)
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<CostCentre> CostCentres => Set<CostCentre>();
    public DbSet<Commodity> Commodities => Set<Commodity>();
    public DbSet<Load> Loads => Set<Load>();
    public DbSet<LoadLeg> LoadLegs => Set<LoadLeg>();
    public DbSet<LoadStatusHistory> LoadStatusHistories => Set<LoadStatusHistory>();
    public DbSet<CommodityLine> CommodityLines => Set<CommodityLine>();

    // Rating (§08)
    public DbSet<RateLine> RateLines => Set<RateLine>();

    // Privacy (§14)
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    // Audit (§12) — append-only; see AuditSaveChangesInterceptor
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RoleFunction>()
            .HasKey(rf => new { rf.RoleId, rf.FunctionId });

        modelBuilder.Entity<Client>().Property(c => c.CreditLimit).HasPrecision(18, 2);
        modelBuilder.Entity<CommodityLine>().Property(c => c.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<RateLine>().Property(r => r.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<RateLine>().Property(r => r.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<RateLine>().Property(r => r.RatePerUnit).HasPrecision(18, 4);

        modelBuilder.Entity<Load>()
            .HasMany(l => l.Legs)
            .WithOne()
            .HasForeignKey(leg => leg.LoadId);

        modelBuilder.Entity<LoadLeg>()
            .HasMany(leg => leg.CommodityLines)
            .WithOne()
            .HasForeignKey(cl => cl.LoadLegId);

        ApplyTenancyScopeFilters(modelBuilder);
    }

    /// <summary>
    /// Applies the Tenant/Company global query filter to every entity deriving from
    /// TenantScopedEntity / CompanyScopedEntity, without needing one hand-written
    /// HasQueryFilter call per entity (docs/architecture.html §4.1, §4.3).
    /// </summary>
    private void ApplyTenancyScopeFilters(ModelBuilder modelBuilder)
    {
        var tenantFilterMethod = typeof(TmsDbContext)
            .GetMethod(nameof(SetTenantScopedFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
        var companyFilterMethod = typeof(TmsDbContext)
            .GetMethod(nameof(SetCompanyScopedFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clrType = entityType.ClrType;

            if (typeof(CompanyScopedEntity).IsAssignableFrom(clrType))
            {
                companyFilterMethod.MakeGenericMethod(clrType).Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(TenantScopedEntity).IsAssignableFrom(clrType))
            {
                tenantFilterMethod.MakeGenericMethod(clrType).Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private void SetTenantScopedFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : TenantScopedEntity
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => IsPlatformSupportBypass || e.TenantId == CurrentTenantId);
    }

    private void SetCompanyScopedFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : CompanyScopedEntity
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => IsPlatformSupportBypass
                || (e.TenantId == CurrentTenantId && (CurrentCompanyId == null || e.CompanyId == CurrentCompanyId)));
    }
}
