// Copyright (c) Duende Software. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace IdentityServerHost;

/// <summary>
/// Configuration for which Microsoft Entra ID workloads may register clients through DCR.
/// </summary>
public class EntraWorkloadOptions
{
    /// <summary>
    /// The Entra tenant (directory) id that tokens must be issued for.
    /// </summary>
    public string AllowedTenantId { get; set; } = string.Empty;

    /// <summary>
    /// The object ids (oid) of the managed identities allowed to register.
    /// </summary>
    public string[] AllowedObjectIds { get; set; } = [];

    /// <summary>
    /// The API scopes a dynamically registered workload may be granted.
    /// </summary>
    public string[] AllowedScopes { get; set; } = [];
}
