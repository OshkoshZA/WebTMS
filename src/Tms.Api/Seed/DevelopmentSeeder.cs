using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tms.Infrastructure;
using Tms.Modules.Identity;
using Tms.Shared;

namespace Tms.Api.Seed;

/// <summary>
/// Development-only bootstrap data — a Tenant, Company, one admin user with a
/// Company-scoped role (docs/architecture.html §04, §07), and the Function catalog
/// (§07) — so a freshly-created database has something to log in as. Never runs
/// outside the Development environment.
/// </summary>
public static class DevelopmentSeeder
{
    public const string AdminEmail = "admin@demo.local";
    public const string AdminPassword = "DemoAdmin#2026";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<object>>();

        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync())
        {
            await SeedTenantCompanyAndAdminAsync(scope.ServiceProvider, db, logger);
        }

        await EnsureFunctionCatalogAsync(db, roleManager, logger);
    }

    private static async Task SeedTenantCompanyAndAdminAsync(IServiceProvider sp, TmsDbContext db, ILogger logger)
    {
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();

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

        if (await roleManager.FindByNameAsync("Admin") is null)
        {
            await roleManager.CreateAsync(new ApplicationRole { Name = "Admin", TenantId = tenant.Id });
        }
        var adminRole = await roleManager.FindByNameAsync("Admin")
            ?? throw new InvalidOperationException("Admin role was not created.");

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

        logger.LogInformation(
            "Seeded development data — Tenant '{Tenant}', Company '{Company}'. Log in with {Email} / {Password}",
            tenant.Name, company.LegalName, AdminEmail, AdminPassword);
    }

    /// <summary>
    /// Registers the known Function catalog (§07: "new functions are registered by
    /// the API as new endpoints ship") and grants the demo Admin role whatever it
    /// needs. Runs every startup, not just on first seed, so a function added after
    /// the database already exists still gets created and granted with no manual step.
    /// </summary>
    private static async Task EnsureFunctionCatalogAsync(TmsDbContext db, RoleManager<ApplicationRole> roleManager, ILogger logger)
    {
        var knownFunctions = new[]
        {
            ("client.creditlimit.override",
             "Push a load or commodity line through over a client's credit limit, with a logged reason (§5.4).")
        };

        var adminRole = await roleManager.FindByNameAsync("Admin");

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
                logger.LogInformation("Granted function '{Function}' to role 'Admin'", code);
            }
        }
    }
}
