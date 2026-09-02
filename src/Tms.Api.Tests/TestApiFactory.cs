using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tms.Api.Tests;

/// <summary>
/// Boots the real API (WebApplicationFactory&lt;Program&gt;, real DI, real SQL Server —
/// no mocks) with two rate limits raised for the test run: the auth-endpoint limit
/// (Program.cs, §11.1, 10/min per IP in production — every fixture and race test logs
/// in from the same loopback IP) and the global per-user limit embedded in each staff
/// JWT at issuance (JwtTokenService, 300/min in production — the whole suite runs
/// through one shared, pre-authenticated staff session and comfortably clears that in
/// well under a minute). Both are sized for one real caller, not a full test run's
/// worth of traffic funneled through a single session.
/// </summary>
internal static class TestApiFactory
{
    public static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:AuthPermitLimit"] = "1000",
                    ["RateLimiting:DefaultUserPermitLimit"] = "100000"
                })));
}
