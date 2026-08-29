using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tms.Modules.Audit;
using Tms.Modules.Billing;
using Tms.Modules.Debrief;
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
///
/// Also implements <see cref="IDataProtectionKeyContext"/> for field-level encryption
/// (§12/§14.5) — see the doc comment on <see cref="DataProtectionKeys"/>.
/// </summary>
public class TmsDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IDataProtectionKeyContext
{
    private readonly ITenantContext _tenantContext;
    private readonly IDataProtector _bankingProtector;

    public TmsDbContext(DbContextOptions<TmsDbContext> options, ITenantContext tenantContext, IDataProtectionProvider dataProtectionProvider)
        : base(options)
    {
        _tenantContext = tenantContext;

        // A versioned purpose string (§14.5) — Data Protection derives a
        // cryptographically independent key per distinct purpose from the same key
        // ring, so a future second protected field (or a deliberate re-key to "v2")
        // can never cross-contaminate with this one.
        _bankingProtector = dataProtectionProvider.CreateProtector("Tms.BankingDetails.v1");
    }

    /// <summary>
    /// Backs ASP.NET Core Data Protection's PersistKeysToDbContext (Tms.Api's
    /// Program.cs) — the key ring lives in this same database, sharing its
    /// backup/restore durability story (§15), rather than a per-machine filesystem
    /// folder that wouldn't survive a redeploy or a second app instance. On this
    /// Windows dev box the key ring is encrypted at rest via DPAPI — ASP.NET Core's
    /// automatic default when no explicit key-protection is configured. A real
    /// deployment still needs an explicit .ProtectKeysWithAzureKeyVault(...) or
    /// equivalent so the key ring itself is wrapped by a real KMS-held key rather than
    /// OS-level protection tied to one machine — the one piece of this still not
    /// production-real, the same "structurally correct now, real once it exists"
    /// pattern as CreditExposureService's AR Outstanding was before Billing existed.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

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
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();
    public DbSet<ApiClientSecret> ApiClientSecrets => Set<ApiClientSecret>();
    public DbSet<ApiClientRole> ApiClientRoles => Set<ApiClientRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Fleet (§5.1)
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();

    // Loads (§5.1, §5.2, §5.5)
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientCurrency> ClientCurrencies => Set<ClientCurrency>();
    public DbSet<Subcontractor> Subcontractors => Set<Subcontractor>();
    public DbSet<SubcontractorCurrency> SubcontractorCurrencies => Set<SubcontractorCurrency>();
    public DbSet<CostCentre> CostCentres => Set<CostCentre>();
    public DbSet<Commodity> Commodities => Set<Commodity>();
    public DbSet<Load> Loads => Set<Load>();
    public DbSet<LoadLeg> LoadLegs => Set<LoadLeg>();
    public DbSet<LoadStatusHistory> LoadStatusHistories => Set<LoadStatusHistory>();
    public DbSet<CommodityLine> CommodityLines => Set<CommodityLine>();
    public DbSet<LoadConfirmation> LoadConfirmations => Set<LoadConfirmation>();

    // Rating (§08)
    public DbSet<RateLine> RateLines => Set<RateLine>();

    // Debrief (§09, §9.1)
    public DbSet<Modules.Debrief.Debrief> Debriefs => Set<Modules.Debrief.Debrief>();
    public DbSet<DebriefIncident> DebriefIncidents => Set<DebriefIncident>();
    public DbSet<ExpenseType> ExpenseTypes => Set<ExpenseType>();
    public DbSet<DebriefExpense> DebriefExpenses => Set<DebriefExpense>();

    // Billing (§10.3 financial calendar, §10.1 sell-side Invoice/InvoiceLine, §10.2 buy-side payables; credit notes (§10.1) land in a later phase)
    public DbSet<FinancialYear> FinancialYears => Set<FinancialYear>();
    public DbSet<FinancialPeriod> FinancialPeriods => Set<FinancialPeriod>();
    public DbSet<DebtorsAgingSnapshot> DebtorsAgingSnapshots => Set<DebtorsAgingSnapshot>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<CreditNoteLine> CreditNoteLines => Set<CreditNoteLine>();
    public DbSet<SubcontractorAccrual> SubcontractorAccruals => Set<SubcontractorAccrual>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SubcontractorExpense> SubcontractorExpenses => Set<SubcontractorExpense>();

    // Privacy (§14)
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    // Audit (§12) — append-only; see AuditSaveChangesInterceptor
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RoleFunction>()
            .HasKey(rf => new { rf.RoleId, rf.FunctionId });

        // Field-level encryption for banking/PII (§12, §14.5) — transparent to every
        // caller: EF Core encrypts on write and decrypts on read via these converters,
        // so no controller or service needs to know BankingDetails is ciphertext at
        // rest. nvarchar(max) already backs both columns, so ciphertext's larger size
        // than plaintext needed no migration of the column itself.
        var bankingDetailsConverter = new ValueConverter<string, string>(
            plaintext => _bankingProtector.Protect(plaintext),
            ciphertext => _bankingProtector.Unprotect(ciphertext));
        var nullableBankingDetailsConverter = new ValueConverter<string?, string?>(
            plaintext => plaintext == null ? null : _bankingProtector.Protect(plaintext),
            ciphertext => ciphertext == null ? null : _bankingProtector.Unprotect(ciphertext));

        modelBuilder.Entity<Company>().Property(c => c.BankingDetails).HasConversion(bankingDetailsConverter);
        modelBuilder.Entity<Subcontractor>().Property(s => s.BankingDetails).HasConversion(nullableBankingDetailsConverter);

        modelBuilder.Entity<Client>().Property(c => c.CreditLimit).HasPrecision(18, 2);
        modelBuilder.Entity<CommodityLine>().Property(c => c.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<RateLine>().Property(r => r.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<RateLine>().Property(r => r.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<RateLine>().Property(r => r.RatePerUnit).HasPrecision(18, 4);

        // A client/subcontractor's currency allow-list (§4.3) — each row a currency
        // beyond the party's own primary CurrencyId, which is always implicitly
        // allowed and so never needs a row here.
        modelBuilder.Entity<ClientCurrency>().Property(cc => cc.CreditLimit).HasPrecision(18, 2);
        modelBuilder.Entity<ClientCurrency>()
            .HasIndex(cc => new { cc.ClientId, cc.CurrencyId })
            .IsUnique()
            .HasDatabaseName("ClientCurrencyIndex");
        modelBuilder.Entity<SubcontractorCurrency>()
            .HasIndex(sc => new { sc.SubcontractorId, sc.CurrencyId })
            .IsUnique()
            .HasDatabaseName("SubcontractorCurrencyIndex");

        modelBuilder.Entity<Load>()
            .HasMany(l => l.Legs)
            .WithOne()
            .HasForeignKey(leg => leg.LoadId);

        modelBuilder.Entity<LoadLeg>()
            .HasMany(leg => leg.CommodityLines)
            .WithOne()
            .HasForeignKey(cl => cl.LoadLegId);

        modelBuilder.Entity<FinancialYear>()
            .HasMany(y => y.Periods)
            .WithOne()
            .HasForeignKey(p => p.FinancialYearId);

        // Exactly one Open period per company (§10.3) is enforced here, not just by
        // FinancialYearsController.Create/FinancialPeriodsController.Close's own
        // read-then-write checks — two concurrent calls could otherwise both read "no
        // Open period yet" before either committed, opening two at once. A filtered
        // unique index only constrains rows where Status = Open (1), so as many
        // Future/Closed periods as needed can still share a CompanyId freely.
        modelBuilder.Entity<FinancialPeriod>()
            .HasIndex(p => p.CompanyId)
            .IsUnique()
            .HasFilter("[Status] = 1")
            .HasDatabaseName("FinancialPeriodOneOpenPerCompanyIndex");

        modelBuilder.Entity<DebtorsAgingSnapshot>().Property(s => s.CurrentAmount).HasPrecision(18, 2);
        modelBuilder.Entity<DebtorsAgingSnapshot>().Property(s => s.Days30).HasPrecision(18, 2);
        modelBuilder.Entity<DebtorsAgingSnapshot>().Property(s => s.Days60).HasPrecision(18, 2);
        modelBuilder.Entity<DebtorsAgingSnapshot>().Property(s => s.Days90).HasPrecision(18, 2);
        modelBuilder.Entity<DebtorsAgingSnapshot>().Property(s => s.Days90Plus).HasPrecision(18, 2);
        modelBuilder.Entity<DebtorsAgingSnapshot>().Property(s => s.TotalOutstanding).HasPrecision(18, 2);

        modelBuilder.Entity<Invoice>()
            .HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey(l => l.InvoiceId);

        modelBuilder.Entity<Invoice>().Property(i => i.TotalExVat).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(i => i.VatAmount).HasPrecision(18, 2);
        modelBuilder.Entity<Invoice>().Property(i => i.TotalIncVat).HasPrecision(18, 2);
        modelBuilder.Entity<InvoiceLine>().Property(l => l.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<InvoiceLine>().Property(l => l.Rate).HasPrecision(18, 4);
        modelBuilder.Entity<InvoiceLine>().Property(l => l.Amount).HasPrecision(18, 2);

        // A sell RateLine can be billed at most once — protects against two
        // concurrent InvoicesController.Generate calls both reading the same line as
        // unbilled before either committed.
        modelBuilder.Entity<InvoiceLine>()
            .HasIndex(l => l.RateLineSellId)
            .IsUnique()
            .HasDatabaseName("InvoiceLineRateLineSellIndex");

        modelBuilder.Entity<CreditNote>()
            .HasMany(cn => cn.Lines)
            .WithOne()
            .HasForeignKey(l => l.CreditNoteId);

        modelBuilder.Entity<CreditNote>().Property(cn => cn.TotalAmount).HasPrecision(18, 2);
        modelBuilder.Entity<CreditNoteLine>().Property(l => l.Amount).HasPrecision(18, 2);

        modelBuilder.Entity<SupplierInvoice>()
            .HasMany(si => si.Expenses)
            .WithOne()
            .HasForeignKey(e => e.SupplierInvoiceId);

        // Duplicate-supplier-invoice protection: the same carrier can't have their own
        // invoice number captured twice for the same Company. An app-layer AnyAsync
        // check in SupplierInvoicesController.Create gives a clean 409 in the common
        // case; this is the real guarantee under concurrent Create calls (same pattern
        // as RoleNameIndex/UserNameIndex — see the comment above those).
        modelBuilder.Entity<SupplierInvoice>()
            .HasIndex(si => new { si.CompanyId, si.SubcontractorId, si.SupplierInvoiceNumber })
            .IsUnique()
            .HasDatabaseName("SupplierInvoiceNumberIndex");

        modelBuilder.Entity<SubcontractorAccrual>().Property(a => a.EstimatedAmount).HasPrecision(18, 2);
        modelBuilder.Entity<SupplierInvoice>().Property(si => si.Amount).HasPrecision(18, 2);
        modelBuilder.Entity<SubcontractorExpense>().Property(e => e.Amount).HasPrecision(18, 2);

        // An accrual can be netted at most once — protects against two concurrent
        // SupplierInvoicesController.Match calls both reading the same accrual as
        // still Accrued before either committed.
        modelBuilder.Entity<SubcontractorExpense>()
            .HasIndex(e => e.AccrualId)
            .IsUnique()
            .HasDatabaseName("SubcontractorExpenseAccrualIndex");

        // Defense in depth alongside LoadsController's per-leg SQL application lock
        // (§5.2, §8.2, §10.2): a leg gets at most one LoadConfirmation and each buy
        // RateLine at most one SubcontractorAccrual, even if some future code path
        // ever raised either of these without holding that lock.
        modelBuilder.Entity<LoadConfirmation>()
            .HasIndex(lc => lc.LoadLegId)
            .IsUnique()
            .HasDatabaseName("LoadConfirmationLegIndex");
        modelBuilder.Entity<SubcontractorAccrual>()
            .HasIndex(a => a.RateLineBuyId)
            .IsUnique()
            .HasDatabaseName("SubcontractorAccrualRateLineIndex");

        // A leg gets at most one Debrief — never resubmitted, only ever approved once
        // (§09).
        modelBuilder.Entity<Modules.Debrief.Debrief>()
            .HasIndex(d => d.LoadLegId)
            .IsUnique()
            .HasDatabaseName("DebriefLoadLegIndex");

        modelBuilder.Entity<Modules.Debrief.Debrief>()
            .HasMany(d => d.Incidents)
            .WithOne()
            .HasForeignKey(i => i.DebriefId);
        modelBuilder.Entity<Modules.Debrief.Debrief>()
            .HasMany(d => d.Expenses)
            .WithOne()
            .HasForeignKey(e => e.DebriefId);

        modelBuilder.Entity<Modules.Debrief.Debrief>().Property(d => d.OdometerStart).HasPrecision(18, 2);
        modelBuilder.Entity<Modules.Debrief.Debrief>().Property(d => d.OdometerEnd).HasPrecision(18, 2);
        modelBuilder.Entity<Modules.Debrief.Debrief>().Property(d => d.FuelLitres).HasPrecision(18, 2);
        modelBuilder.Entity<Modules.Debrief.Debrief>().Property(d => d.FuelCost).HasPrecision(18, 2);
        modelBuilder.Entity<Modules.Debrief.Debrief>().Property(d => d.DrivingHours).HasPrecision(18, 2);
        modelBuilder.Entity<DebriefExpense>().Property(e => e.Amount).HasPrecision(18, 2);

        ApplyTenancyScopeFilters(modelBuilder);

        // ApplicationUser/ApplicationRole carry a TenantId but extend IdentityUser<Guid>/
        // IdentityRole<Guid>, not TenantScopedEntity, so the reflection-based pass above
        // never reaches them — they need their own filter, applied here by hand. Unlike
        // every other tenant-scoped filter, this one also bypasses when CurrentTenantId
        // is itself null: Login/Refresh query these tables before any tenant is known
        // (that's exactly what they're resolving), and the same is true of the
        // Development seeder running outside any HTTP request. Once a tenant *is*
        // resolved (any authenticated request), the filter applies as normal — e.g. it
        // stops ApiClientsController.Create from wiring an ApiClientRole to a RoleId
        // that belongs to a different tenant.
        modelBuilder.Entity<ApplicationUser>()
            .HasQueryFilter(u => IsPlatformSupportBypass || CurrentTenantId == null || u.TenantId == CurrentTenantId);
        modelBuilder.Entity<ApplicationRole>()
            .HasQueryFilter(r => IsPlatformSupportBypass || CurrentTenantId == null || r.TenantId == CurrentTenantId);

        // Identity's default RoleNameIndex/UserNameIndex are single-column unique
        // constraints spanning every row in the table — correct for a single-tenant
        // app, wrong here: they made a role/username collide across two completely
        // different Tenants (confirmed live — a second tenant's "Regional Manager"
        // role blocked the first tenant from ever using that name, crashing
        // RolesController.Create with an unhandled 500). Free up each original name
        // and reuse it for a composite (TenantId, NormalizedX) unique index instead,
        // so uniqueness is enforced per Tenant, matching every query filter above.
        modelBuilder.Entity<ApplicationRole>(b =>
        {
            b.HasIndex(r => r.NormalizedName).IsUnique(false).HasDatabaseName("RoleNormalizedNameIndex");
            b.HasIndex(r => new { r.TenantId, r.NormalizedName })
                .IsUnique()
                .HasDatabaseName("RoleNameIndex")
                .HasFilter("[NormalizedName] IS NOT NULL");
        });

        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.HasIndex(u => u.NormalizedUserName).IsUnique(false).HasDatabaseName("UserNormalizedUserNameIndex");
            b.HasIndex(u => new { u.TenantId, u.NormalizedUserName })
                .IsUnique()
                .HasDatabaseName("UserNameIndex")
                .HasFilter("[NormalizedUserName] IS NOT NULL");
        });
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
