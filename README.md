# Transport Management System

A multi-country, multi-tenant SaaS platform for managing loads across owned fleet and subcontracted capacity — vehicle and driver masters, commodity-level rating, cost centre allocation, driver debriefing, credit control, and a full API for external integration.

## Stack

- **Frontend**: Vue 3 + TypeScript + Vite (internal app + Customer Portal + Supplier Portal)
- **API**: ASP.NET Core 8 (C#)
- **Database**: Microsoft SQL Server, via EF Core
- **Integration**: REST API, webhooks, first-party Xero accounting adapter

## Documentation

The full architecture and design specification lives in [`docs/architecture.html`](docs/architecture.html) — open it in a browser. It covers the domain model, multi-tenancy and Row-Level Security, rating and subcontractor accrual accounting, invoicing and the financial calendar, portals, audit trail, data retention/GDPR-POPIA compliance, backup and disaster recovery, and the delivery roadmap.

## Solution structure

```
src/
  Tms.Api                 ASP.NET Core host — controllers, auth, JWT, Swagger
  Tms.Infrastructure       EF Core DbContext, migrations, tenant scoping filters
  Tms.Shared               Base entity types, tenant context, Country/Currency/UoM
  Tms.Modules.Identity     Tenant, Company, Users, Roles, Functions
  Tms.Modules.Fleet        Vehicle, Driver, Location
  Tms.Modules.Loads        Client, Load, LoadLeg, Commodity, CostCentre
  Tms.Modules.Rating       RateLine
  Tms.Modules.Audit        AuditEntry + the SaveChanges interceptor that writes it
  Tms.Modules.Privacy      RetentionPolicy
  Tms.Modules.Debrief      Phase 2 — not yet implemented
  Tms.Modules.Billing      Phase 2/3 — not yet implemented
  Tms.Modules.Integration  Phase 3 — not yet implemented
  Tms.Modules.Exceptions   Phase 2/3 — not yet implemented
web/
  tms-app                  Internal ops/dispatch/finance SPA
  tms-customer-portal      Client-scoped SPA
  tms-supplier-portal      Subcontractor-scoped SPA
```

## Getting started

**API**

```bash
dotnet restore
dotnet build
```

You'll need a local SQL Server (or `mssql` in Docker) and to point
`src/Tms.Api/appsettings.Development.json` at it — the checked-in
`ConnectionStrings:Default` is a localhost placeholder, not a real one.
Then create the database:

```bash
dotnet tool restore
dotnet ef database update --project src/Tms.Infrastructure --startup-project src/Tms.Infrastructure
dotnet run --project src/Tms.Api
```

**Frontend** (each of the three apps under `web/`)

```bash
cd web/tms-app
npm install
npm run dev
```

## What's actually implemented vs. scaffolded

This is a Phase 1 skeleton (see the roadmap in `docs/architecture.html`), not a
finished build:

- **Real**: the full project/module structure, core Phase 1 entities (Tenant,
  Company, Client, Vehicle, Driver, Load, LoadLeg, Commodity, RateLine, CostCentre),
  ASP.NET Core Identity wiring, JWT auth scaffolding, the Tenant/Company global
  query filter (the application-layer half of §4.1's isolation model), the audit
  `SaveChanges` interceptor, and a first EF Core migration.
- **Deliberately deferred**: SQL Server Row-Level Security itself is a deployment
  script, not C#, and isn't included yet. The credit-limit hard stop (§5.4) has a
  `TODO` marking exactly where it plugs in once Billing exists. Debrief, Billing,
  Integration, and Exceptions are empty module projects — present so the solution
  shape matches the design doc, with no entities yet.

## Status

Early scaffold — buildable, not yet runnable end-to-end (no database has been created against it, and authentication issuance isn't wired up yet).
