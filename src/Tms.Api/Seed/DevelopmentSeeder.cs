using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
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
            try
            {
                (tenant, company) = await SeedTenantAndCompanyAsync(db);
                logger.LogInformation("Seeded development Tenant '{Tenant}' and Company '{Company}'.", tenant.Name, company.LegalName);
            }
            catch (DbUpdateException)
            {
                // Lost a startup race against another concurrent app instance seeding the
                // exact same fixed reference-data ids (§ below) — Tms.Api.Tests boots more
                // than one WebApplicationFactory<Program> instance against the same
                // database, and xUnit runs different collections in parallel by default,
                // so two instances' very first SeedAsync call can both see "no Tenant yet"
                // before either commits. Whichever one loses falls back to what the winner
                // actually created instead of treating this as a real failure — the same
                // unique-index-plus-catch(DbUpdateException) pattern already used
                // elsewhere in this codebase for exactly this class of race.
                db.ChangeTracker.Clear();
                isFirstRun = false;
                tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync();
                company = await db.Companies.IgnoreQueryFilters().FirstAsync(c => c.TenantId == tenant.Id);
            }
        }

        await EnsureStarterRolesAsync(db, roleManager, tenant!.Id, logger);
        await EnsureFunctionCatalogAsync(db, roleManager, logger);

        if (isFirstRun)
        {
            await SeedAdminUserAsync(scope.ServiceProvider, db, tenant!, company!, roleManager, logger);
        }

        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        await EncryptLegacyPlaintextBankingDetailsAsync(db, dataProtectionProvider, logger);
    }

    /// <summary>
    /// One-time fix-up, dev-environment only: BankingDetails on Company (always seeded)
    /// and Subcontractor (set ad hoc during earlier live testing this project has gone
    /// through) were both written as plain text before field-level encryption existed
    /// (§14.5). Runs every startup but is cheap and self-limiting — a value that
    /// already round-trips through Unprotect cleanly is left untouched, so each table
    /// only ever gets real work done on it once. Reads/writes the raw column via ADO.NET
    /// rather than through TmsDbContext's own encrypting value converter, deliberately —
    /// going through the converter would try to Unprotect a legacy plaintext value on
    /// simple materialization and throw before this method ever got a chance to fix it.
    /// A real production rollout of encryption onto an existing dataset needs exactly
    /// this kind of one-time re-encryption pass, just as a proper migration/admin tool
    /// rather than app-startup code confined to two known tables/fields.
    /// </summary>
    private static async Task EncryptLegacyPlaintextBankingDetailsAsync(TmsDbContext db, IDataProtectionProvider dataProtectionProvider, ILogger logger)
    {
        var protector = dataProtectionProvider.CreateProtector("Tms.BankingDetails.v1");

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await EncryptLegacyPlaintextColumnAsync(connection, protector, "Companies", logger);
        await EncryptLegacyPlaintextColumnAsync(connection, protector, "Subcontractors", logger);
    }

    private static async Task EncryptLegacyPlaintextColumnAsync(
        System.Data.Common.DbConnection connection, IDataProtector protector, string tableName, ILogger logger)
    {
        var pending = new List<(Guid Id, string Raw)>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = $"SELECT Id, BankingDetails FROM {tableName} WHERE BankingDetails IS NOT NULL";
            await using var reader = await selectCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                pending.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        foreach (var (id, raw) in pending)
        {
            try
            {
                protector.Unprotect(raw); // already encrypted — nothing to do
            }
            catch (CryptographicException)
            {
                var encrypted = protector.Protect(raw);

                await using var updateCommand = connection.CreateCommand();
                updateCommand.CommandText = $"UPDATE {tableName} SET BankingDetails = @value WHERE Id = @id";

                var valueParam = updateCommand.CreateParameter();
                valueParam.ParameterName = "@value";
                valueParam.Value = encrypted;
                updateCommand.Parameters.Add(valueParam);

                var idParam = updateCommand.CreateParameter();
                idParam.ParameterName = "@id";
                idParam.Value = id;
                updateCommand.Parameters.Add(idParam);

                await updateCommand.ExecuteNonQueryAsync();
                logger.LogInformation("Encrypted legacy plaintext BankingDetails for {Table} {Id}.", tableName, id);
            }
        }
    }

    /// <summary>
    /// Fixed, well-known ids for the reference-data rows Tms.Api.Tests hardcodes as
    /// fixture constants (StaffTestFixture/PortalTestFixture) — not randomly generated,
    /// unlike everything else this method seeds. Before this fix, every one of these
    /// entities (where seeded at all — CostCentre/Location/Commodity/Vehicle/Driver
    /// weren't seeded here at all) got the default client-side Guid.NewGuid(), which
    /// only ever "worked" for the test suite by accident: those specific ids happened to
    /// match rows created ad hoc, at some point, directly against this project's own
    /// long-lived local dev database — never reproducible on any other one, which is
    /// exactly what broke this project's very first CI run once one existed (every
    /// fixture's very first real POST, referencing one of these ids, 404ing against a
    /// row that had never actually been created on a truly fresh database).
    /// </summary>
    private static async Task<(Tenant Tenant, Company Company)> SeedTenantAndCompanyAsync(TmsDbContext db)
    {
        var country = new Country { Code = "ZA", Name = "South Africa" };
        var currency = new Currency { Id = Guid.Parse("2366a0f6-9b2d-41c0-9d73-2d38d0e45e8b"), Code = "ZAR", Name = "South African Rand", Symbol = "R" };
        // A second currency, granted to a Client/Subcontractor via its currency allow-list
        // (§4.3) in the AddCurrency tests — deliberately not the Company's own primary.
        var secondaryCurrency = new Currency { Id = Guid.Parse("983cc062-2b8a-41d4-9209-a4b05f6dcc1d"), Code = "USD", Name = "US Dollar", Symbol = "$" };
        db.Countries.Add(country);
        db.Currencies.Add(currency);
        db.Currencies.Add(secondaryCurrency);

        var unitOfMeasure = new UnitOfMeasure { Id = Guid.Parse("a155c6f5-8dde-41f3-a54d-0ccdfd02d7cd"), Code = "PER_LOAD", Description = "Per load" };
        db.UnitsOfMeasure.AddRange(
            unitOfMeasure,
            new UnitOfMeasure { Code = "PER_KM", Description = "Per kilometre" },
            new UnitOfMeasure { Code = "PER_TON", Description = "Per ton" },
            new UnitOfMeasure { Code = "PER_PALLET", Description = "Per pallet" },
            new UnitOfMeasure { Code = "PER_HOUR", Description = "Per hour" },
            new UnitOfMeasure { Code = "PER_LITRE", Description = "Per litre" });

        db.LoadTypes.AddRange(
            new Tms.Modules.Loads.LoadType { Id = Guid.Parse("6C48E708-7D45-4381-881D-16CC9E39ED24"), Code = "FTL", Description = "Full truckload" },
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

        db.CostCentres.Add(new Tms.Modules.Loads.CostCentre
        {
            Id = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000003"),
            TenantId = tenant.Id,
            CompanyId = company.Id,
            Code = "DEMO-CC",
            Name = "Demo Cost Centre"
        });
        db.Locations.AddRange(
            new Tms.Modules.Fleet.Location
            {
                Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                TenantId = tenant.Id,
                CompanyId = company.Id,
                Name = "Demo Origin",
                Province = "Gauteng",
                CountryId = country.Id
            },
            new Tms.Modules.Fleet.Location
            {
                Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
                TenantId = tenant.Id,
                CompanyId = company.Id,
                Name = "Demo Destination",
                Province = "Western Cape",
                CountryId = country.Id
            });
        db.Commodities.Add(new Tms.Modules.Loads.Commodity
        {
            Id = Guid.Parse("4cf021f4-50e1-4532-b7a4-627035eadef6"),
            TenantId = tenant.Id,
            CompanyId = company.Id,
            Code = "DEMO-CMD",
            Name = "Demo Commodity",
            DefaultUnitOfMeasureId = unitOfMeasure.Id
        });
        db.Vehicles.Add(new Tms.Modules.Fleet.Vehicle
        {
            Id = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            TenantId = tenant.Id,
            CompanyId = company.Id,
            FleetNo = "DEMO-01",
            Registration = "DEMO001GP",
            Type = Tms.Modules.Fleet.VehicleType.Horse
        });
        db.Drivers.Add(new Tms.Modules.Fleet.Driver
        {
            Id = Guid.Parse("a05273b3-36b7-454a-9029-7b09a3068db0"),
            TenantId = tenant.Id,
            CompanyId = company.Id,
            EmployeeNo = "DEMO-EMP-01",
            Name = "Demo Driver",
            LicenceCode = "C1"
        });

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
    private static async Task EnsureStarterRolesAsync(TmsDbContext db, RoleManager<ApplicationRole> roleManager, Guid tenantId, ILogger logger)
    {
        foreach (var roleName in new[] { AdminRoleName, IntegrationServiceRoleName })
        {
            if (await roleManager.FindByNameAsync(roleName) is null)
            {
                try
                {
                    await roleManager.CreateAsync(new ApplicationRole { Name = roleName, TenantId = tenantId });
                    logger.LogInformation("Created starter role '{Role}'.", roleName);
                }
                catch (DbUpdateException)
                {
                    // Same startup race as SeedTenantAndCompanyAsync — another concurrent
                    // app instance (Tms.Api.Tests boots more than one WebApplicationFactory
                    // against the same database) created this role first.
                    db.ChangeTracker.Clear();
                }
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
            ("client.currency.change",
             "Grant a Client an additional allowed currency (with its own credit limit) or update that limit (§4.3) — separate from client.master.manage since it changes what a client can even be billed in, not just its master-data fields."),
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
            ("finance.creditnote.approve",
             "Create, issue, and void a CreditNote — correcting an Issued invoice or a standalone goodwill adjustment (§10.1) — distinct from finance.invoice.manage since it reduces recognised revenue rather than just raising it."),
            ("subcontractor.master.manage",
             "Create, update, and deactivate Subcontractor master data, including banking details and payment terms (§5.1, §10.2)."),
            ("finance.subcontractorinvoice.process",
             "Capture a subcontractor's SupplierInvoice and match it against open accruals, finalizing the SubcontractorExpense (§10.2) — an AP function, since it recognises a payable."),
            ("company.master.manage",
             "Update a Company's own master data — legal/trading name, registration/VAT numbers, addresses, banking details, invoice numbering, logo, InvoicingEnabled (§5.1, §06)."),
            ("vehicle.master.manage",
             "Create, update, deactivate, and reactivate Vehicle master data (§5.1)."),
            ("driver.master.manage",
             "Create, update, deactivate, and reactivate Driver master data (§5.1)."),
            ("location.master.manage",
             "Create, update, deactivate, and reactivate Location master data (§5.1)."),
            ("costcentre.master.manage",
             "Create, update, deactivate, and reactivate CostCentre master data, including its parent hierarchy (§06)."),
            ("commodity.master.manage",
             "Create, update, deactivate, and reactivate Commodity master data (§5.5)."),
            ("debrief.approve",
             "Resolve a PendingReview Debrief — a Debrief Clerk function, distinct from submitting one, which any authenticated user (or driver mobile web) can do (§09)."),
            ("expensetype.master.manage",
             "Create, update, deactivate, and reactivate ExpenseType master data (§9.1)."),
            ("exception.manage",
             "Acknowledge and resolve an Exception raised by any module against the shared §16.1 mechanism."),
            ("subcontractor.contact.manage",
             "Create, deactivate, and reactivate a Subcontractor's Supplier Portal contacts (§13.1) — distinct from subcontractor.master.manage, since granting a carrier's staff a portal login is a different kind of sensitive than editing the subcontractor's own master data."),
            ("portal.subcontractor.viewlegs",
             "A Supplier Portal contact's own view of their Subcontractor's allocated legs, load confirmations, and accrual/supplier-invoice status (§13.3) — never granted to internal staff, whose equivalent access needs no separate function."),
            ("portal.subcontractor.acknowledgeconfirmation",
             "A Supplier Portal contact accepting or declining their own Subcontractor's Load Confirmation (§8.2, §13.3)."),
            ("portal.subcontractor.uploadpod",
             "A Supplier Portal contact submitting their own Subcontractor's leg debrief — POD and any additional claims (§09, §13.3)."),
            ("client.contact.manage",
             "Create, deactivate, and reactivate a Client's Customer Portal contacts (§13.1) — distinct from client.master.manage, since granting a customer's staff a portal login is a different kind of sensitive than editing the client's own master data."),
            ("portal.client.viewloads",
             "A Customer Portal contact's own view of their Client's loads, tracking, and credit status (§13.2) — never granted to internal staff, whose equivalent access needs no separate function."),
            ("portal.client.viewinvoices",
             "A Customer Portal contact's own view of their Client's invoices and credit notes (§13.2)."),
            ("portal.client.createload",
             "A Customer Portal contact's self-service load booking (§13.2) — still subject to the same credit hard stop as every other channel (§5.4); no special exemption.")
        };

        var adminRole = await roleManager.FindByNameAsync(AdminRoleName);

        foreach (var (code, description) in knownFunctions)
        {
            var function = await db.Functions.FirstOrDefaultAsync(f => f.Code == code);
            if (function is null)
            {
                function = new Function { Code = code, Description = description };
                db.Functions.Add(function);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Same startup race as SeedTenantAndCompanyAsync — another concurrent
                    // app instance registered this Function first.
                    db.ChangeTracker.Clear();
                    function = await db.Functions.FirstAsync(f => f.Code == code);
                }
            }

            if (adminRole is null) continue; // no demo tenant seeded yet — nothing to grant to

            var alreadyGranted = await db.RoleFunctions
                .AnyAsync(rf => rf.RoleId == adminRole.Id && rf.FunctionId == function.Id);
            if (!alreadyGranted)
            {
                db.RoleFunctions.Add(new RoleFunction { RoleId = adminRole.Id, FunctionId = function.Id });
                try
                {
                    await db.SaveChangesAsync();
                    logger.LogInformation("Granted function '{Function}' to role '{Role}'", code, AdminRoleName);
                }
                catch (DbUpdateException)
                {
                    // Same race — another concurrent app instance granted this function
                    // to the role first.
                    db.ChangeTracker.Clear();
                }
            }
        }
    }
}
