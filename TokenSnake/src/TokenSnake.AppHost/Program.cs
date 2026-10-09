// Launcher only: the hosts do not depend on Aspire. Ports are fixed because the
// redirect URIs, CORS origins, issuer and CSP are derived from these URLs.
var builder = DistributedApplication.CreateBuilder(args);

var identityServer = builder.AddProject<Projects.TokenSnake_IdentityServer>("identityserver")
    .WithHttpsEndpoint(port: 5001, isProxied: false);

var web = builder.AddProject<Projects.TokenSnake_Web>("web")
    .WithHttpsEndpoint(port: 5002, isProxied: false);

var authority = identityServer.GetEndpoint("https");
var gameOrigin = web.GetEndpoint("https");

identityServer.WithEnvironment("TokenSnake__GameOrigin", gameOrigin);

web.WithEnvironment("TokenSnake__Authority", authority)
    .WithEnvironment("TokenSnake__GameOrigin", gameOrigin)
    .WaitFor(identityServer);

builder.Build().Run();
