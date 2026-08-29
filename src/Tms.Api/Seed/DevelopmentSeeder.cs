using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Api.Seed;

/// <summary>
/// Development-only bootstrap data — a Tenant, Company, one admin user with a
/// Company-scoped role (docs/architecture.html §04, §07), the starter roles, and
/// the Function catalog (§07) — so a freshly-created database has something to
/// log in as. Never runs outside the Development environment.
///
/// Only the Tenant/Company/reference-data/admin-user step is genuinely one-time;
/// starter roles and the Function catalog are re-synced on every startup, so a role
/// or function added after the database already exists still gets created (and, for
/// functions, granted) without a manual step against an already-seeded database —
/// exactly the situation this file used to get wrong before both were split out.
/// </summary>
public static class DevelopmentSeeder
{
    public const string AdminEmail = "admin@demo.local";
    public const string AdminPassword = "DemoAdmin#2026";

    private const string AdminRoleName = "Admin";
    private const string IntegrationServiceRoleName = "Integration Service";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<object>>();

        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync();
        var isFirstRun = tenant is null;

        Company? company = null;
        if (isFirstRun)
        {
            (tenant, company) = await SeedTenantAndCompanyAsync(db);
            logger.LogInformation("Seeded development Tenant '{Tenant}' and Company '{Company}'.", tenant.Name, company.LegalName);
        }

        await EnsureStarterRolesAsync(roleManager, tenant!.Id, logger);
        await EnsureFunctionCatalogAsync(db, roleManager, logger);

        if (isFirstRun)
        {
            await SeedAdminUserAsync(scope.ServiceProvider, db, tenant!, company!, roleManager, logger);
        }
    }

    private static async Task<(Tenant Tenant, Company Company)> SeedTenantAndCompanyAsync(TmsDbContext db)
    {
        var country = new Country { Code = "ZA", Name = "South Africa" };
        var currency = new Currency { Code = "ZAR", Name = "South African Rand", Symbol = "R" };
        db.Countries.Add(country);
        db.Currencies.Add(currency);

        db.UnitsOfMeasure.AddRange(
            new UnitOfMeasure { Code = "PER_LOAD", Description = "Per load" },
            new UnitOfMeasure { Code = "PER_KM", Description = "Per kilometre" },
            new UnitOfMeasure { Code = "PER_TON", Description = "Per ton" },
            new UnitOfMeasure { Code = "PER_PALLET", Description = "Per pallet" },
            new UnitOfMeasure { Code = "PER_HOUR", Description = "Per hour" },
            new UnitOfMeasure { Code = "PER_LITRE", Description = "Per litre" });

        db.LoadTypes.AddRange(
            new Tms.Modules.Loads.LoadType { Code = "FTL", Description = "Full truckload" },
            new Tms.Modules.Loads.LoadType { Code = "LTL", Description = "Less than truckload" },
            new Tms.Modules.Loads.LoadType { Code = "BULK", Description = "Bulk" });

        var tenant = new Tenant { Name = "Demo Tenant", PlanTier = "Trial" };
        db.Tenants.Add(tenant);

        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = "Demo Transport (Pty) Ltd",
            RegistrationNo = "2026/000000/07",
            VatNumber = "4000000000",
            PhysicalAddress = "1 Demo Street, Johannesburg",
            PostalAddress = "PO Box 1, Johannesburg",
            BankingDetails = "Demo Bank, Acc 000000000",
            InvoiceNumberPrefix = "INV",
            CountryId = country.Id,
            CurrencyId = currency.Id
        };
        db.Companies.Add(company);

        await db.SaveChangesAsync();
        return (tenant, company);
    }

    private static async Task SeedAdminUserAsync(
        IServiceProvider sp, TmsDbContext db, Tenant tenant, Company company, RoleManager<ApplicationRole> roleManager, ILogger logger)
    {
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var adminRole = await roleManager.FindByNameAsync(AdminRoleName)
            ?? throw new InvalidOperationException("Admin role was not created by EnsureStarterRolesAsync.");

        var adminUser = new ApplicationUser
        {
            UserName = AdminEmail,
            Email = AdminEmail,
            EmailConfirmed = true,
            TenantId = tenant.Id,
            DisplayName = "Demo Admin"
        };
        var createResult = await userManager.CreateAsync(adminUser, AdminPassword);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to seed the development admin user: " +
                string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        db.UserCompanyRoles.Add(new UserCompanyRole
        {
            UserId = adminUser.Id,
            CompanyId = company.Id,
            RoleId = adminRole.Id
        });

        await db.SaveChangesAsync();

        logger.LogInformation("Seeded admin user. Log in with {Email} / {Password}", AdminEmail, AdminPassword);
    }

    /// <summary>
    /// Ensures the demo tenant's starter roles (§07) exist — Admin for interactive
    /// staff, Integration Service for machine clients (§11.1) — without granting
    /// either any functions by default. Runs every startup.
    /// </summary>
    private static async Task EnsureStarterRolesAsync(RoleManager<ApplicationRole> roleManager, Guid tenantId, ILogger logger)
    {
        foreach (var roleName in new[] { AdminRoleName, IntegrationServiceRoleName })
        {
            if (await roleManager.FindByNameAsync(roleName) is null)
            {
                await roleManager.CreateAsync(new ApplicationRole { Name = roleName, TenantId = tenantId });
                logger.LogInformation("Created starter role '{Role}'.", roleName);
            }
        }
    }

    /// <summary>
    /// Registers the known Function catalog (§07: "new functions are registered by
    /// the API as new endpoints ship") and grants the demo Admin role whatever it
    /// needs. Runs every startup, so a function added after the database already
    /// exists still gets created and granted with no manual step.
    /// </summary>
    private static async Task EnsureFunctionCatalogAsync(TmsDbContext db, RoleManager<ApplicationRole> roleManager, ILogger logger)
    {
        var knownFunctions = new[]
        {
            ("client.creditlimit.override",
             "Push a load or commodity line through over a client's credit limit, with a logged reason (§5.4)."),
            ("client.master.manage",
             "Create, update, and deactivate Client master data, including its credit limit and payment terms (§11.2)."),
            ("integration.apiclient.manage",
             "Create, rotate secrets for, and revoke system-to-system API clients (§11.1)."),
            ("identity.user.manage",
             "Create, update, deactivate/reactivate internal users, and manage their company/role assignments (§07)."),
            ("identity.role.manage",
             "Create roles and grant/revoke the functions they carry (§07) — distinct from identity.user.manage, since defining what a role can do is more sensitive than assigning an existing one."),
            ("finance.calendar.manage",
             "Create a Company's FinancialYears and their periods (§10.3)."),
            ("finance.period.close",
             "Close a FinancialPeriod, opening the next one and rolling every Client's debtors aging forward (§10.3) — one-directional, like every other approval boundary in this design."),
            ("finance.invoice.manage",
             "Generate, issue, and void sell-side invoices (§10.1)."),
            ("subcontractor.master.manage",
             "Create, update, and deactivate Subcontractor master data, including banking details and payment terms (§5.1, §10.2)."),
            ("finance.subcontractorinvoice.process",
             "Capture a subcontractor's SupplierInvoice and match it against open accruals, finalizing the SubcontractorExpense (§10.2) — an AP function, since it recognises a payable.")
        };

        var adminRole = await roleManager.FindByNameAsync(AdminRoleName);

        foreach (var (code, description) in knownFunctions)
        {
            var function = await db.Functions.FirstOrDefaultAsync(f => f.Code == code);
            if (function is null)
            {
                function = new Function { Code = code, Description = description };
                db.Functions.Add(function);
                await db.SaveChangesAsync();
            }

            if (adminRole is null) continue; // no demo tenant seeded yet — nothing to grant to

            var alreadyGranted = await db.RoleFunctions
                .AnyAsync(rf => rf.RoleId == adminRole.Id && rf.FunctionId == function.Id);
            if (!alreadyGranted)
            {
                db.RoleFunctions.Add(new RoleFunction { RoleId = adminRole.Id, FunctionId = function.Id });
                await db.SaveChangesAsync();
                logger.LogInformation("Granted function '{Function}' to role '{Role}'", code, AdminRoleName);
            }
        }
    }
}
