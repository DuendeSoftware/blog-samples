// Copyright (c) Duende Software. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Duende.IdentityServer.Configuration.Models;
using Duende.IdentityServer.Configuration.Models.DynamicClientRegistration;
using Duende.IdentityServer.Configuration.Validation.DynamicClientRegistration;

namespace IdentityServerHost;

/// <summary>
/// Gates and shapes dynamically registered clients based on the Microsoft Entra ID
/// workload identity that authenticated to the DCR endpoint. The authenticated caller
/// (the workload's Entra token) is available as <c>context.Caller</c>.
/// </summary>
public class EntraWorkloadDcrValidator : DynamicClientRegistrationValidator
{
    private readonly EntraWorkloadOptions _options;
    private readonly ILogger<EntraWorkloadDcrValidator> _logger;

    public EntraWorkloadDcrValidator(
        EntraWorkloadOptions options,
        ILogger<DynamicClientRegistrationValidator> baseLogger,
        ILogger<EntraWorkloadDcrValidator> logger)
        : base(baseLogger)
    {
        _options = options;
        _logger = logger;
    }

    // Only allow the client_credentials grant. A workload authenticates as itself,
    // machine to machine, so authorization_code and other interactive grants have no place here.
    protected override async Task<IStepResult> SetGrantTypesAsync(
        DynamicClientRegistrationContext context, CancellationToken ct)
    {
        // Reject anything that is not an application-only (workload) token. Entra sets
        // idtyp to "app" for client-credentials tokens and "user" for signed-in users.
        // As a fallback for tokens without idtyp, an app-only token also carries no
        // scp (scope) claim.
        var identityType = context.Caller.FindFirst("idtyp")?.Value;
        var hasUserScopes = context.Caller.HasClaim(c => c.Type == "scp");
        if (identityType != "app" && (identityType is not null || hasUserScopes))
        {
            _logger.LogWarning("Rejected DCR request from a non-application identity (idtyp {IdType})", identityType);
            return await StepResult.Failure(
                "Only workload identities may register", "invalid_client_metadata");
        }

        // The Entra tenant the token was issued for.
        var tenantId = context.Caller.FindFirst("tid")?.Value;
        if (tenantId != _options.AllowedTenantId)
        {
            _logger.LogWarning("Rejected DCR request from tenant {TenantId}", tenantId);
            return await StepResult.Failure(
                "Registration is not allowed for this tenant", "invalid_client_metadata");
        }

        // The workload's own object id (the managed identity's service principal).
        var objectId = context.Caller.FindFirst("oid")?.Value;
        if (objectId is null || !_options.AllowedObjectIds.Contains(objectId))
        {
            _logger.LogWarning("Rejected DCR request from object id {ObjectId}", objectId);
            return await StepResult.Failure(
                "This workload is not allowed to register", "invalid_client_metadata");
        }

        // Force machine-to-machine only. Ignore whatever grant types the request asked for.
        context.Client.AllowedGrantTypes = ["client_credentials"];

        return await StepResult.Success();
    }

    // Restrict the scopes a dynamically registered workload can obtain, and set
    // conservative token defaults.
    protected override async Task<IStepResult> SetScopesAsync(
        DynamicClientRegistrationContext context, CancellationToken ct)
    {
        var requested = context.Request.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        ?? [];

        var granted = requested.Intersect(_options.AllowedScopes).ToArray();
        foreach (var scope in granted)
        {
            context.Client.AllowedScopes.Add(scope);
        }

        // Short-lived tokens and no refresh tokens for a machine client.
        context.Client.AccessTokenLifetime = 300;
        context.Client.AllowOfflineAccess = false;

        return await StepResult.Success();
    }

    // Name the client after the workload so it is recognizable in logs and storage.
    protected override async Task<IStepResult> SetClientNameAsync(
        DynamicClientRegistrationContext context, CancellationToken ct)
    {
        var objectId = context.Caller.FindFirst("oid")?.Value;
        context.Client.ClientName = context.Request.ClientName ?? $"workload-{objectId}";

        return await StepResult.Success();
    }
}
