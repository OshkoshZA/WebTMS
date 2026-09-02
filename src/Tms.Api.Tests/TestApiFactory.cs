using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tms.Api.Tests;

/// <summary>
/// Boots the real API (WebApplicationFactory&lt;Program&gt;, real DI, real SQL Server —
/// no mocks) with the auth-endpoint rate limit (Program.cs, §11.1, 10/min per IP in
/// production) raised, since every fixture and race test in this project logs in from
/// the same loopback IP and production's limit is sized for a single real client, not
/// a full test run's worth of concurrent logins.
/// </summary>
internal static class TestApiFactory
{
    public static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:AuthPermitLimit"] = "1000"
                })));
}
