// Copyright (c) Duende Software. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Core;
using Azure.Identity;
using Duende.IdentityModel.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var app = builder.Build();

// Endpoints and audience come from configuration so the same build runs locally and in
// Azure App Service. See appsettings.json.
var identityServer = app.Configuration["Workload:IdentityServer"]!;
var simpleApi = app.Configuration["Workload:SimpleApi"]!;
var dcrAudience = app.Configuration["Workload:DcrAudience"]!;

app.MapGet("/", () => Results.Content(
    "POST or GET /register-and-call to register this workload and call the API.", "text/plain"));

// Runs the full flow on request: get an Entra token, register through DCR, request an
// access token with the new client, and call the API. Returns the result as JSON so you
// can browse to it once the app is deployed.
app.MapGet("/register-and-call", async (IHttpClientFactory httpFactory) =>
{
    var http = httpFactory.CreateClient();

    // Get a Microsoft Entra ID token for this workload's managed identity.
    // DefaultAzureCredential uses the managed identity when running in Azure, and falls
    // back to your `az login` session during local development.
    var credential = new DefaultAzureCredential();
    var entraToken = await credential.GetTokenAsync(
        new TokenRequestContext([dcrAudience]));

    // Register a client through DCR, presenting the Entra token as the bearer.
    var registration = await http.RegisterClientAsync(new DynamicClientRegistrationRequest
    {
        Address = $"{identityServer}/connect/dcr",
        Token = entraToken.Token,
        Document = new DynamicClientRegistrationDocument
        {
            ClientName = "orders-service",
            GrantTypes = { "client_credentials" },
            Scope = "simple-api"
        }
    });

    if (registration.IsError)
    {
        return Results.Problem($"Registration failed: {registration.Error}");
    }

    // Use the freshly minted client credentials to get an access token for the API.
    var disco = await http.GetDiscoveryDocumentAsync(identityServer);
    var tokenResponse = await http.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
    {
        Address = disco.TokenEndpoint,
        ClientId = registration.ClientId!,
        ClientSecret = registration.ClientSecret!,
        Scope = "simple-api"
    });

    if (tokenResponse.IsError)
    {
        return Results.Problem($"Token request failed: {tokenResponse.Error}");
    }

    // Call the API with the access token.
    http.SetBearerToken(tokenResponse.AccessToken!);
    var apiResponse = await http.GetStringAsync($"{simpleApi}/identity");

    return Results.Json(new
    {
        registeredClientId = registration.ClientId,
        apiResponse
    });
});

app.Run();
