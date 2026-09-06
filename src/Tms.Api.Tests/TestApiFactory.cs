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
///
/// Also disables BackgroundJobs (§11.3) — WebhookRetryHostedService's own timer would
/// otherwise start ticking against the same rows a test is busy asserting on. A test
/// that wants the sweep logic itself resolves WebhookRetryJob from the factory's
/// services and calls RunOnceAsync directly instead.
/// </summary>
internal static class TestApiFactory
{
    /// <summary>
    /// <paramref name="extraConfig"/> layers additional overrides on top of the usual
    /// three (applied last, so it can override them too if a test genuinely needs to) —
    /// for EmailTestFixture's own per-fixture SmtpTestReceiver port, which can't be a
    /// fixed value the way the other overrides are.
    /// </summary>
    public static WebApplicationFactory<Program> Create(IReadOnlyDictionary<string, string?>? extraConfig = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var overrides = new Dictionary<string, string?>
                {
                    ["RateLimiting:AuthPermitLimit"] = "1000",
                    ["RateLimiting:DefaultUserPermitLimit"] = "100000",
                    ["BackgroundJobs:Enabled"] = "false"
                };
                if (extraConfig is not null)
                {
                    foreach (var (key, value) in extraConfig) overrides[key] = value;
                }
                config.AddInMemoryCollection(overrides);
            }));
}
