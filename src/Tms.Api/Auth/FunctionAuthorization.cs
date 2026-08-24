using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Tms.Api.Auth;

/// <summary>
/// Docs/architecture.html §07: "Every API endpoint declares the function(s) it
/// requires; middleware checks the caller's resolved function set from their
/// role(s)." A Function claim (type "function") is embedded in the JWT at login
/// (see AuthController), one per function the caller's role grants for the
/// company they're operating in (Role → RoleFunction → Function, §5.1).
/// </summary>
public class FunctionRequirement : IAuthorizationRequirement
{
    public string FunctionCode { get; }
    public FunctionRequirement(string functionCode) => FunctionCode = functionCode;
}

public class FunctionAuthorizationHandler : AuthorizationHandler<FunctionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FunctionRequirement requirement)
    {
        if (context.User.HasClaim("function", requirement.FunctionCode))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Synthesizes an authorization policy for any function code on demand — a new
/// function never needs a policy registered by hand at startup; it just needs a
/// Function row (and a RoleFunction grant) to exist, matching §07's "new functions
/// are registered by the API as new endpoints ship" rather than hard-coded per role.
/// Usage: <c>[Authorize(Policy = "client.creditlimit.override")]</c>, or
/// programmatically via <c>IAuthorizationService.AuthorizeAsync(user, "the.function.code")</c>.
/// </summary>
public class FunctionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public FunctionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new FunctionRequirement(policyName))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
