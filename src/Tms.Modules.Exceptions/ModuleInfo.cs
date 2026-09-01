// Tms.Modules.Exceptions
//
// §16.1: ExceptionRecord — the shared cross-module attention mechanism feeding all
// dashboards. Raised by ExceptionService (src/Tms.Api/Services) from three sources so
// far: a PendingReview Debrief (§09), a credit hard-stop override (§5.4), and an
// accrual/invoice variance on a matched SupplierInvoice (§10.2). The other three
// sources Fig. 13 names — accounting sync failure, vehicle/driver compliance expiry,
// DSR deadline approaching — have no owning module yet (Tms.Modules.Integration is
// still an empty scaffold, Tms.Modules.Privacy has no DataSubjectRequest workflow, and
// compliance-expiry scanning has no scheduled job anywhere in this codebase), so they
// aren't wired in; each can call ExceptionService.Raise once its own module exists.
// Scoping is company-wide (internal staff) only — the ClientContact/SubcontractorContact
// scoped views §16.1 also describes need the Customer/Supplier Portal identity types
// (§13), which don't exist yet either.
