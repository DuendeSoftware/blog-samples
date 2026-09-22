// Copyright (c) Duende Software. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Duende.IdentityServer.Models;

namespace IdentityServerHost;

public static class Config
{
    // The API scope our workload will ultimately request a token for.
    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope("simple-api", "Simple API")
    ];

    public static IEnumerable<ApiResource> ApiResources =>
    [
        new ApiResource("simple-api", "Simple API")
        {
            Scopes = { "simple-api" }
        }
    ];
}
