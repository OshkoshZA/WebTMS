using Tms.Shared;
using Xunit;

namespace Tms.Api.Tests;

/// <summary>
/// Pure unit tests for the root-cause logic behind this project's one critical
/// security bug (2463fac): CanAccessSubcontractor/CanAccessClient must treat "my own
/// scoping field is null" as unrestricted staff ONLY when the OTHER field is null
/// too — a Client contact's own SubcontractorId being null must never be mistaken for
/// staff access to subcontractor-scoped data, and symmetrically for a Subcontractor
/// contact against client-scoped data. No HTTP/DB involved — if this table ever goes
/// red, the fix belongs in PortalScopeExtensions itself, not in any one controller.
/// </summary>
public class PortalScopeExtensionsTests
{
    private static readonly Guid SubcontractorA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SubcontractorB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClientB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId => Guid.Empty;
        public Guid? CompanyId => Guid.Empty;
        public bool IsPlatformSupport => false;
        public Guid? SubcontractorId { get; init; }
        public Guid? ClientId { get; init; }
    }

    [Fact]
    public void Staff_with_neither_field_set_can_access_any_subcontractor()
    {
        var staff = new FakeTenantContext();
        Assert.True(staff.CanAccessSubcontractor(SubcontractorA));
        Assert.True(staff.CanAccessSubcontractor(SubcontractorB));
    }

    [Fact]
    public void Staff_with_neither_field_set_can_access_any_client()
    {
        var staff = new FakeTenantContext();
        Assert.True(staff.CanAccessClient(ClientA));
        Assert.True(staff.CanAccessClient(ClientB));
    }

    [Fact]
    public void Subcontractor_contact_can_access_only_their_own_subcontractor()
    {
        var contact = new FakeTenantContext { SubcontractorId = SubcontractorA };
        Assert.True(contact.CanAccessSubcontractor(SubcontractorA));
        Assert.False(contact.CanAccessSubcontractor(SubcontractorB));
    }

    [Fact]
    public void Client_contact_can_access_only_their_own_client()
    {
        var contact = new FakeTenantContext { ClientId = ClientA };
        Assert.True(contact.CanAccessClient(ClientA));
        Assert.False(contact.CanAccessClient(ClientB));
    }

    /// <summary>The exact bug: a Client contact's own SubcontractorId is null, exactly like staff's — CanAccessSubcontractor must not mistake one for the other.</summary>
    [Fact]
    public void Client_contact_can_access_no_subcontractor_at_all()
    {
        var contact = new FakeTenantContext { ClientId = ClientA };
        Assert.False(contact.CanAccessSubcontractor(SubcontractorA));
        Assert.False(contact.CanAccessSubcontractor(SubcontractorB));
    }

    /// <summary>The symmetric case: a Subcontractor contact's own ClientId is null, exactly like staff's — CanAccessClient must not mistake one for the other.</summary>
    [Fact]
    public void Subcontractor_contact_can_access_no_client_at_all()
    {
        var contact = new FakeTenantContext { SubcontractorId = SubcontractorA };
        Assert.False(contact.CanAccessClient(ClientA));
        Assert.False(contact.CanAccessClient(ClientB));
    }
}
