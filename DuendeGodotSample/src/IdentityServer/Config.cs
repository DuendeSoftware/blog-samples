using Duende.IdentityServer.Models;
using Duende.IdentityServer.Test;
using System.Security.Claims;

namespace IdentityServer;

public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email()
    ];

    public static IEnumerable<ApiScope> ApiScopes => Array.Empty<ApiScope>();

    public static IEnumerable<Client> Clients =>
    [
        // Client 1: Authorization Code + PKCE (system browser flow)
        new Client
        {
            ClientId = "godot-browser-client",
            ClientName = "Godot Browser Auth",
            AllowedGrantTypes = GrantTypes.Code,
            RequireClientSecret = false,   // Public client — native/desktop app
            RequirePkce = true,
            RedirectUris = { "http://localhost:8948/callback" },
            PostLogoutRedirectUris = { "http://localhost:8948/callback" },
            AllowedScopes = { "openid", "profile", "email" },
            AllowOfflineAccess = false
        },

        // Client 2: Device Authorization Grant (universal / console-friendly flow)
        new Client
        {
            ClientId = "godot-device-client",
            ClientName = "Godot Device Auth",
            AllowedGrantTypes = GrantTypes.DeviceFlow,
            RequireClientSecret = false,   // Public client
            AllowedScopes = { "openid", "profile", "email" },
            AllowOfflineAccess = false
        }
    ];

    public static List<TestUser> TestUsers => new()
    {
        new TestUser
        {
            SubjectId = "1",
            Username = "alice",
            Password = "alice",
            Claims = new[]
            {
                new Claim("name", "Alice Smith"),
                new Claim("email", "alice@example.com"),
                new Claim("email_verified", "true")
            }
        },
        new TestUser
        {
            SubjectId = "2",
            Username = "bob",
            Password = "bob",
            Claims = new[]
            {
                new Claim("name", "Bob Jones"),
                new Claim("email", "bob@example.com"),
                new Claim("email_verified", "true")
            }
        }
    };
}
