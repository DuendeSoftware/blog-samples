# TokenSnake research notes

Verified 2026-10-08.

## Pinned versions

| Package | Version | Notes |
|---|---|---|
| Duende.IdentityServer | 8.0.9 | net10.0 only |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.12 | |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | |
| Microsoft.Extensions.TimeProvider.Testing | 10.10.0 | |
| xunit.v3 | 4.0.1 | use xunit.v3 only, not v2 as well |
| xunit.runner.visualstudio | 4.0.0 | |

## Duende IdentityServer v8 API notes

- **Extension grants**: `IExtensionGrantValidator.ValidateAsync(ExtensionGrantValidationContext, CancellationToken)`. Register with `AddExtensionGrantValidator<T>()`. Custom request parameters are read from `context.Request.Raw`. ([validator](https://docs.duendesoftware.com/identityserver/reference/v8/validators/extension-grant-validator/), [guide](https://docs.duendesoftware.com/identityserver/tokens/extension-grants/))
- **Custom token response parameters**: `GrantValidationResult` takes a `customResponse` dictionary, which is returned in the token response. ([docs](https://docs.duendesoftware.com/identityserver/reference/v8/models/grant-validation-result/))
- **Per-request lifetime**: `ICustomTokenRequestValidator.ValidateAsync` has a `CancellationToken` in v8 and can change `ValidatedRequest.AccessTokenLifetime` (seconds). Do not use it to pass state. ([docs](https://docs.duendesoftware.com/identityserver/reference/v8/validators/custom-token-request-validator/))
- **Token validation**: `ITokenValidator.ValidateAccessTokenAsync(token, expectedScope, ct)`; pass `expectedScope` null for none.
- **Profile service**: `IProfileService` reads claims from `context.Subject`. Claims passed in `GrantValidationResult` flow to the profile service. ([docs](https://docs.duendesoftware.com/identityserver/reference/v8/services/profile-service/))
- **Clients**: `RequireClientSecret = false` for public clients; `AllowedGrantTypes` can include the custom grant; `AllowedCorsOrigins` takes origin only (no path); call `UseCors` after `UseIdentityServer` if both are used; `RequirePkce` defaults to true for auth code. ([client](https://docs.duendesoftware.com/identityserver/reference/v8/models/client/), [CORS](https://docs.duendesoftware.com/identityserver/tokens/cors/))
- **Custom login**: set `UserInteraction.LoginUrl`, then `HttpContext.SignInAsync` with `IdentityServerUser`. ([login](https://docs.duendesoftware.com/identityserver/ui/login/), [local](https://docs.duendesoftware.com/identityserver/ui/login/local/), [sample](https://github.com/DuendeSoftware/IdentityServer/blob/main/bff/hosts/Hosts.IdentityServer/Pages/Account/Login/Index.cshtml.cs))
- **Licence**: free for dev/test. With no key there is no request limit but rate-limited warnings are logged. Set the `LicenseKey` option or the config keys `Duende:IdentityServer:LicenseKey` / `Duende:LicenseKey`. ([licensing](https://docs.duendesoftware.com/general/licensing/))
- **v8 changes**: .NET 10 only, cancellation-token signatures, `ITokenValidator` signature, POST redirects use 303. ([upgrade](https://docs.duendesoftware.com/identityserver/upgrades/v7_4-to-v8_0/))
- **To verify by integration test**: that a secretless client (`RequireClientSecret=false`) can use the custom grant. The docs don't show this exact combination.

## Sensitive form parameters

Add custom parameters to `IdentityServerOptions.Logging.TokenRequestSensitiveValuesFilter`. Add `subject_token` so tokens aren't logged.

## Verified URLs (HTTP 200, checked 2026-10-08)

- https://docs.duendesoftware.com/identityserver/reference/v8/validators/extension-grant-validator/
- https://docs.duendesoftware.com/identityserver/tokens/extension-grants/
- https://docs.duendesoftware.com/identityserver/reference/v8/models/grant-validation-result/
- https://docs.duendesoftware.com/identityserver/reference/v8/validators/custom-token-request-validator/
- https://docs.duendesoftware.com/identityserver/reference/v8/services/profile-service/
- https://docs.duendesoftware.com/identityserver/reference/v8/models/client/
- https://docs.duendesoftware.com/identityserver/reference/v8/options/
- https://docs.duendesoftware.com/identityserver/tokens/cors/
- https://docs.duendesoftware.com/identityserver/ui/login/
- https://docs.duendesoftware.com/identityserver/ui/login/local/
- https://docs.duendesoftware.com/identityserver/ui/custom/
- https://docs.duendesoftware.com/identityserver/fundamentals/users/
- https://docs.duendesoftware.com/identityserver/upgrades/v7_4-to-v8_0/
- https://docs.duendesoftware.com/identityserver/reference/v8/endpoints/token/
- https://docs.duendesoftware.com/general/licensing/
- https://docs.duendesoftware.com/identityserver/ (checked 2026-10-09)
- https://duendesoftware.com/products/identityserver (checked 2026-10-09, title "Duende IdentityServer | Duende")
- https://docs.duendesoftware.com/bff/
- https://docs.duendesoftware.com/bff/fundamentals/tokens/
- https://docs.duendesoftware.com/accesstokenmanagement/web-apps/
- https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
- https://datatracker.ietf.org/doc/rfc10017/ (RFC 10017, "OAuth 2.0 for Browser-Based Applications", BCP, Aug 2026)
- https://datatracker.ietf.org/doc/rfc9700/
- https://github.com/DuendeSoftware/IdentityServer/blob/main/bff/hosts/Hosts.IdentityServer/Pages/Account/Login/Index.cshtml.cs

## Blog links (verified, 2026-10-08)

All return HTTP 200. Only these links may be used in the blog post.

- Extension Grants: https://docs.duendesoftware.com/identityserver/tokens/extension-grants/
- Profile Service: https://docs.duendesoftware.com/identityserver/reference/v8/services/profile-service/
- Login UI: https://docs.duendesoftware.com/identityserver/ui/login/
- Accepting Local Credentials: https://docs.duendesoftware.com/identityserver/ui/login/local/
- Custom Pages: https://docs.duendesoftware.com/identityserver/ui/custom/
- Authentication Session (IdentityServerUser / SignInAsync): https://docs.duendesoftware.com/identityserver/ui/login/session/
- Clients: https://docs.duendesoftware.com/identityserver/fundamentals/clients/
- Client reference (RequireClientSecret, AllowedGrantTypes, AccessTokenLifetime, RequirePkce; there is no separate PKCE guide): https://docs.duendesoftware.com/identityserver/reference/v8/models/client/
- CORS: https://docs.duendesoftware.com/identityserver/tokens/cors/
- Custom Token Request Validator: https://docs.duendesoftware.com/identityserver/reference/v8/validators/custom-token-request-validator/
- Using JWTs: https://docs.duendesoftware.com/identityserver/apis/aspnetcore/jwt/
- Licensing: https://docs.duendesoftware.com/general/licensing/
- BFF overview: https://docs.duendesoftware.com/bff/
- BFF Token Management: https://docs.duendesoftware.com/bff/fundamentals/tokens/
- How BFF Helps Secure Single Page Applications: https://duendesoftware.com/learn/how-bff-helps-secure-single-page-applications
- The Backend for Frontend Pattern Is Now Official IETF Guidance: RFC 10017 Published: https://duendesoftware.com/blog/the-backend-for-frontend-pattern-is-now-official-ietf-guidance-rfc-10017-published
- RFC 10017 (BCP 212, Aug 2026, published): https://datatracker.ietf.org/doc/rfc10017/
- RFC 9700: https://datatracker.ietf.org/doc/rfc9700/
- Duende proxy/load balancer docs: https://docs.duendesoftware.com/identityserver/deployment/#proxy-servers-and-load-balancers
- Proxy guidance: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0
- Duende Backend For Frontend (BFF) product page: https://duendesoftware.com/products/capabilities/backend-for-frontend
- Duende IdentityServer product page: https://duendesoftware.com/products/identityserver
- Logout page: https://docs.duendesoftware.com/identityserver/ui/logout/
- Optional, DPoP: https://docs.duendesoftware.com/identityserver/tokens/pop/
- RFC 7636, PKCE (verified 2026-10-09): https://datatracker.ietf.org/doc/html/rfc7636
- RFC 6749, OAuth 2.0, section 4.5 extension grants (verified 2026-10-09): https://datatracker.ietf.org/doc/html/rfc6749#section-4.5
- RFC 8693, OAuth 2.0 Token Exchange (verified 2026-10-09): https://datatracker.ietf.org/doc/html/rfc8693
- RFC 7519, JSON Web Token (verified 2026-10-09): https://datatracker.ietf.org/doc/html/rfc7519
- Token endpoint reference: https://docs.duendesoftware.com/identityserver/reference/v8/endpoints/token/
- Requesting tokens: https://docs.duendesoftware.com/identityserver/tokens/requesting/
- Refresh tokens: https://docs.duendesoftware.com/identityserver/tokens/refresh/
- API scopes: https://docs.duendesoftware.com/identityserver/fundamentals/resources/api-scopes/
- API resources: https://docs.duendesoftware.com/identityserver/fundamentals/resources/api-resources/
- Claims: https://docs.duendesoftware.com/identityserver/fundamentals/claims/
- External login providers: https://docs.duendesoftware.com/identityserver/ui/login/external/
- CIBA endpoint reference: https://docs.duendesoftware.com/identityserver/reference/v8/endpoints/ciba/
- Extension grants page has "Token Exchange" and "Impersonation / Delegation" (act claim) sections, so it is the Duende page for RFC 8693.
- PKCE: no dedicated Duende guide. The Client reference documents `RequirePkce`: https://docs.duendesoftware.com/identityserver/reference/v8/models/client/
