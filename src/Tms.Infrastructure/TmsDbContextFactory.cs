using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Tms.Shared;

namespace Tms.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` / `database update` run without a live ASP.NET Core
/// host or a resolvable ITenantContext — design time never needs a real tenant. The
/// connection string comes from Tms.Api's user secrets (the same store `dotnet run`
/// uses), never from a hardcoded value here — see README.md "Getting started".
/// </summary>
public class TmsDbContextFactory : IDesignTimeDbContextFactory<TmsDbContext>
{
    // Tms.Api's UserSecretsId (see src/Tms.Api/Tms.Api.csproj) — shared deliberately so
    // `dotnet ef` and `dotnet run` read the same local connection string.
    private const string TmsApiUserSecretsId = "a6d5553f-0a25-451d-8543-e2dd645cf047";

    public TmsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(TmsApiUserSecretsId)
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? "Server=localhost;Database=Tms;Trusted_Connection=True;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<TmsDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new TmsDbContext(optionsBuilder.Options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? CompanyId => null;
        public bool IsPlatformSupport => true; // bypass filters — migrations operate on schema, not tenant data
    }
}
